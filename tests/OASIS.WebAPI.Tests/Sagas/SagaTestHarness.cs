using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OASIS.WebAPI.Data;
using OASIS.WebAPI.Interfaces;
using OASIS.WebAPI.Sagas;
using OASIS.WebAPI.Tests.TestSupport;

namespace OASIS.WebAPI.Tests.Sagas;

/// <summary>
/// Owns a SQLite-backed saga stack for one test. File-backed by default so the
/// conditional-claim / lease-reclaim arbitration is exercised against a real
/// DB-level write lock (same fidelity rationale as the api-safety-hardening
/// bridge concurrency test — see <see cref="SqliteTestContext"/>).
///
/// <para>The <see cref="EfSagaStore"/> resolves a fresh <c>OASISDbContext</c>
/// per operation via an <see cref="IServiceScopeFactory"/> whose context is
/// bound to this harness's shared physical DB — exactly the production
/// scope-isolation model, exercised against a constraint-enforcing engine.</para>
/// </summary>
public sealed class SagaTestHarness : IDisposable
{
    private readonly SqliteTestContext _db;
    private readonly ServiceProvider _provider;

    public CounterSink Sink { get; } = new();
    public FakeIdempotencyStore Idempotency { get; } = new();
    public IncrementHandler Increment { get; }
    public CompensateHandler Compensate { get; }
    public ISagaStore Store { get; }
    public ISagaCoordinator Coordinator { get; }
    public SagaOptions Options { get; }

    private readonly SagaDefinition _definition;

    public SagaTestHarness(
        RetryPolicy? incrementPolicy = null,
        RetryPolicy? compensatePolicy = null,
        SagaOptions? options = null,
        bool fileBacked = true)
    {
        _db = fileBacked ? SqliteTestContext.FileBacked() : SqliteTestContext.SharedCacheInMemory();
        Options = options ?? new SagaOptions();
        _definition = SampleCounterSaga.Build(incrementPolicy, compensatePolicy);

        Increment = new IncrementHandler(Sink, Idempotency);
        Compensate = new CompensateHandler(Sink, Idempotency);

        var services = new ServiceCollection();
        // Fresh SQLite-backed context per resolved scope, all bound to the one
        // shared physical DB this harness owns.
        services.AddScoped<OASISDbContext>(_ => _db.NewContext());

        // Sample, NON-bridge handlers — the only registration a new consumer
        // needs (proves zero core change).
        services.AddSingleton(Sink);
        services.AddSingleton<IIdempotencyStore>(Idempotency);
        services.AddSingleton<IStepHandler<CounterPayload>>(Increment);

        _provider = services.BuildServiceProvider();
        var scopeFactory = _provider.GetRequiredService<IServiceScopeFactory>();

        Store = new EfSagaStore(scopeFactory);
        var registry = new SagaRegistry(new[] { (ISagaDefinition)_definition });
        Coordinator = new SagaCoordinator(registry, Store);
        _registry = registry;
        _scopeFactory = scopeFactory;
    }

    private readonly ISagaRegistry _registry;
    private readonly IServiceScopeFactory _scopeFactory;

    /// <summary>
    /// Build a processor whose per-tick scope resolves a chosen
    /// <see cref="IStepHandler{CounterPayload}"/> (the increment handler by
    /// default; pass the compensation handler for compensation ticks). Mirrors
    /// the hosted service's "fresh scope per tick" discipline.
    /// </summary>
    public SagaProcessor ProcessorWith(IStepHandler<CounterPayload> handler)
    {
        var services = new ServiceCollection();
        services.AddSingleton(handler);
        var sp = services.BuildServiceProvider();
        return new SagaProcessor(
            Store, _registry, sp, NullLogger<SagaProcessor>.Instance,
            Microsoft.Extensions.Options.Options.Create(Options));
    }

    /// <summary>A processor whose scope can resolve BOTH the increment and the
    /// compensation handler — used when one tick may run a forward step and a
    /// later tick its compensation.</summary>
    public SagaProcessor Processor()
    {
        var services = new ServiceCollection();
        // Last registration wins for the open-generic resolve; register a
        // routing handler that delegates by step name via the sink-aware
        // concrete handlers.
        services.AddSingleton<IStepHandler<CounterPayload>>(
            new RoutingHandler(Increment, Compensate, new MarkDoneHandler(Sink)));
        var sp = services.BuildServiceProvider();
        return new SagaProcessor(
            Store, _registry, sp, NullLogger<SagaProcessor>.Instance,
            Microsoft.Extensions.Options.Options.Create(Options));
    }

    public IServiceScopeFactory ScopeFactory => _scopeFactory;

    public void Dispose()
    {
        _provider.Dispose();
        _db.Dispose();
    }

    /// <summary>Dispatches to the correct concrete handler by step name — the
    /// processor only resolves <see cref="IStepHandler{CounterPayload}"/>, so
    /// this single registration covers all three sample steps.</summary>
    private sealed class RoutingHandler : IStepHandler<CounterPayload>
    {
        private readonly IncrementHandler _inc;
        private readonly CompensateHandler _comp;
        private readonly MarkDoneHandler _done;

        public RoutingHandler(IncrementHandler inc, CompensateHandler comp, MarkDoneHandler done)
        {
            _inc = inc;
            _comp = comp;
            _done = done;
        }

        public Task<StepResult> ExecuteAsync(
            StepExecutionContext<CounterPayload> ctx, CancellationToken ct) => ctx.StepName switch
        {
            SampleCounterSaga.StepIncrement => _inc.ExecuteAsync(ctx, ct),
            SampleCounterSaga.StepCompensate => _comp.ExecuteAsync(ctx, ct),
            _ => _done.ExecuteAsync(ctx, ct),
        };
    }
}
