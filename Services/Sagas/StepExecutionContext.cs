namespace OASIS.WebAPI.Sagas;

/// <summary>
/// What the processor hands a step handler for one attempt. Deliberately small
/// and storage-agnostic: a handler never sees EF, the outbox table, the trigger,
/// or any bridge type — it only sees its typed payload, the correlation key, and
/// the per-step idempotency key it MUST use to gate any irreversible effect.
///
/// <para><b>Idempotency contract.</b> Before performing any irreversible
/// (on-chain / external) effect a handler MUST claim
/// <see cref="StepIdempotencyKey"/> on the shared
/// <see cref="OASIS.WebAPI.Interfaces.IIdempotencyStore"/> (the
/// api-safety-hardening spine) and only proceed on <c>Won == true</c>; on
/// <c>Won == false</c> it replays the recorded outcome. The saga layer does NOT
/// reimplement exactly-once — it composes the existing primitive. The processor
/// guarantees this key is STABLE across crash/retry/reclaim of the same step so
/// a re-run is a genuine idempotent replay, never a second effect.</para>
/// </summary>
/// <typeparam name="TPayload">The step's typed, JSON-serialized payload.</typeparam>
public sealed class StepExecutionContext<TPayload>
{
    public StepExecutionContext(
        string sagaName,
        string stepName,
        string correlationKey,
        string stepIdempotencyKey,
        int attempt,
        TPayload payload)
    {
        SagaName = sagaName;
        StepName = stepName;
        CorrelationKey = correlationKey;
        StepIdempotencyKey = stepIdempotencyKey;
        Attempt = attempt;
        Payload = payload;
    }

    /// <summary>Registered saga definition name (e.g. "sample-counter").</summary>
    public string SagaName { get; }

    /// <summary>The current step's name within the definition.</summary>
    public string StepName { get; }

    /// <summary>
    /// Caller/business correlation key (reuses the api-safety-hardening key
    /// convention). Stable for the whole saga instance — ties every step record
    /// of one run together. NOT itself the idempotency key.
    /// </summary>
    public string CorrelationKey { get; }

    /// <summary>
    /// Stable per-step idempotency key the handler MUST use to gate any
    /// irreversible effect via <c>IIdempotencyStore</c>. Derived from the
    /// correlation key + step name so it is identical across every retry /
    /// reclaim of THIS step, but distinct from sibling steps and the
    /// compensation step (which carries its own key).
    /// </summary>
    public string StepIdempotencyKey { get; }

    /// <summary>1-based attempt number for this invocation.</summary>
    public int Attempt { get; }

    /// <summary>The deserialized step payload.</summary>
    public TPayload Payload { get; }
}

/// <summary>
/// Outcome of one step-handler attempt. The processor maps this to a durable
/// state transition (advance / schedule retry / compensate / dead-letter)
/// exactly the way <c>ReconciliationService</c> maps a chain verdict — the
/// handler itself never touches the store.
/// </summary>
public sealed record StepResult(bool Success, string? Output, string? Error)
{
    /// <summary>The step's irreversible effect is done (or was an idempotent
    /// replay). <paramref name="output"/> is persisted on the record for
    /// observability and may seed the next step.</summary>
    public static StepResult Ok(string? output = null) => new(true, output, null);

    /// <summary>The attempt failed. The processor increments the attempt count
    /// and either schedules a backoff retry or, at the budget, routes to the
    /// declared compensation (forward) / dead-letters (compensation).</summary>
    public static StepResult Fail(string error) => new(false, null, error);
}
