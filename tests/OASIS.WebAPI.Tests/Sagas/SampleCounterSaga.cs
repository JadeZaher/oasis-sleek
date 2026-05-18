using OASIS.WebAPI.Interfaces;
using OASIS.WebAPI.Sagas;

namespace OASIS.WebAPI.Tests.Sagas;

/// <summary>
/// A trivial, completely NON-BRIDGE sample saga proving the module is reusable
/// with ZERO core changes: a 2-step "increment a shared counter, then mark
/// done", plus a compensation step that decrements. A deliberately-failing
/// variant of the first step drives the retry → compensation → dead-letter
/// machinery. The only saga primitives used are the public contracts
/// (<see cref="ISagaDefinition"/>, <see cref="IStepHandler{TPayload}"/>,
/// <see cref="SagaDefinition"/>) and the existing
/// <see cref="IIdempotencyStore"/> — nothing bridge-aware, nothing internal.
/// </summary>
public sealed record CounterPayload(string Bucket, int By);

/// <summary>Thread-safe shared sink the handlers mutate, so a test can assert
/// effects happened exactly once even under retry/reclaim.</summary>
public sealed class CounterSink
{
    private readonly object _gate = new();
    private readonly Dictionary<string, int> _counts = new(StringComparer.Ordinal);
    public int IncrementCalls;
    public int DoneCalls;
    public int CompensationCalls;

    public int Get(string bucket)
    {
        lock (_gate) return _counts.TryGetValue(bucket, out var v) ? v : 0;
    }

    public void Add(string bucket, int by)
    {
        lock (_gate) _counts[bucket] = (_counts.TryGetValue(bucket, out var v) ? v : 0) + by;
    }
}

/// <summary>
/// Step 1: increment the counter, gated on the per-step idempotency key via the
/// shared <see cref="IIdempotencyStore"/> — the irreversible-effect-once
/// contract the spec mandates handlers honour. <see cref="FailUntilAttempt"/>
/// lets a test force N failed attempts before succeeding (or never).
/// </summary>
public sealed class IncrementHandler : IStepHandler<CounterPayload>
{
    private readonly CounterSink _sink;
    private readonly IIdempotencyStore _idem;

    /// <summary>If &gt; 0, fail every attempt strictly before this number;
    /// int.MaxValue ⇒ always fail (drives exhaustion → compensation).</summary>
    public int FailUntilAttempt { get; set; }

    public IncrementHandler(CounterSink sink, IIdempotencyStore idem)
    {
        _sink = sink;
        _idem = idem;
    }

    public async Task<StepResult> ExecuteAsync(
        StepExecutionContext<CounterPayload> ctx, CancellationToken ct)
    {
        Interlocked.Increment(ref _sink.IncrementCalls);

        if (ctx.Attempt < FailUntilAttempt)
            return StepResult.Fail($"forced failure on attempt {ctx.Attempt}");

        // Exactly-once gate on the existing spine (NOT a second mechanism).
        var claim = await _idem.TryClaimAsync(
            ctx.StepIdempotencyKey, "sample_increment", ct);
        if (!claim.Won)
            // Duplicate / resumed step ⇒ idempotent replay, no second effect.
            return StepResult.Ok(claim.Record.ResultPayload);

        _sink.Add(ctx.Payload.Bucket, ctx.Payload.By);
        var output = _sink.Get(ctx.Payload.Bucket).ToString();
        await _idem.CompleteAsync(ctx.StepIdempotencyKey, output, ct);
        return StepResult.Ok(output);
    }
}

/// <summary>Step 2: trivially "mark done" (no external effect — still keys its
/// own idempotency for symmetry).</summary>
public sealed class MarkDoneHandler : IStepHandler<CounterPayload>
{
    private readonly CounterSink _sink;

    public MarkDoneHandler(CounterSink sink) => _sink = sink;

    public Task<StepResult> ExecuteAsync(
        StepExecutionContext<CounterPayload> ctx, CancellationToken ct)
    {
        Interlocked.Increment(ref _sink.DoneCalls);
        return Task.FromResult(StepResult.Ok("done"));
    }
}

/// <summary>Compensation step: decrement (undo the increment). Has its own
/// idempotency key (derived from the compensation step name).</summary>
public sealed class CompensateHandler : IStepHandler<CounterPayload>
{
    private readonly CounterSink _sink;
    private readonly IIdempotencyStore _idem;

    /// <summary>If true, the compensation itself always fails (drives
    /// compensation-exhaustion → dead-letter).</summary>
    public bool AlwaysFail { get; set; }

    public CompensateHandler(CounterSink sink, IIdempotencyStore idem)
    {
        _sink = sink;
        _idem = idem;
    }

    public async Task<StepResult> ExecuteAsync(
        StepExecutionContext<CounterPayload> ctx, CancellationToken ct)
    {
        Interlocked.Increment(ref _sink.CompensationCalls);
        if (AlwaysFail)
            return StepResult.Fail("compensation deliberately failing");

        var claim = await _idem.TryClaimAsync(
            ctx.StepIdempotencyKey, "sample_compensate", ct);
        if (!claim.Won)
            return StepResult.Ok(claim.Record.ResultPayload);

        _sink.Add(ctx.Payload.Bucket, -ctx.Payload.By);
        await _idem.CompleteAsync(ctx.StepIdempotencyKey, "compensated", ct);
        return StepResult.Ok("compensated");
    }
}

/// <summary>Builds the sample saga definition: increment → markDone, with
/// "compensate" declared as the increment step's compensation.</summary>
public static class SampleCounterSaga
{
    public const string Name = "sample-counter";
    public const string StepIncrement = "increment";
    public const string StepMarkDone = "mark-done";
    public const string StepCompensate = "compensate-decrement";

    public static SagaDefinition Build(RetryPolicy? incrementPolicy = null,
        RetryPolicy? compensatePolicy = null) =>
        SagaDefinition.Create(Name)
            .AddForwardStep(new SagaStep<CounterPayload>(
                StepIncrement,
                incrementPolicy ?? RetryPolicy.Default,
                compensationStepName: StepCompensate))
            .AddForwardStep(new SagaStep<CounterPayload>(StepMarkDone))
            .AddCompensationStep(new SagaStep<CounterPayload>(
                StepCompensate, compensatePolicy ?? RetryPolicy.Default))
            .Build();
}
