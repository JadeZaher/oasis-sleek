using System;

namespace Oasis.SurrealDb.Client;

/// <summary>
/// Configuration for a SurrealDB connection / pool. All fields are
/// intentionally JSON-config friendly (string / int / TimeSpan) so consumers
/// can populate from <c>appsettings.json</c> via
/// <c>services.Configure&lt;SurrealConnectionOptions&gt;(Configuration.GetSection("SurrealDb"))</c>.
/// </summary>
/// <remarks>
/// JSON-config defaults are the source-of-truth per the archaeological-persona
/// finding that the wave-1 compose file mis-set durability via env vars: this
/// options shape lives next to the engine boundary and is the only place
/// connection knobs are read.
/// </remarks>
public sealed class SurrealConnectionOptions
{
    /// <summary>
    /// Base HTTP endpoint, e.g. <c>http://localhost:8442</c>. The client
    /// appends <c>/sql</c>, <c>/health</c>, etc. as needed. Required.
    /// </summary>
    public string Endpoint { get; set; } = "http://localhost:8442";

    /// <summary>Namespace scope (the <c>NS</c> header). Required.</summary>
    public string Namespace { get; set; } = "oasis";

    /// <summary>Database scope (the <c>DB</c> header). Required.</summary>
    public string Database { get; set; } = "oasis";

    /// <summary>Basic-auth user. Optional — if both User and Password are
    /// empty, no Authorization header is sent (root anonymous dev mode).</summary>
    public string User { get; set; } = "root";

    /// <summary>Basic-auth password. See <see cref="User"/>.</summary>
    public string Password { get; set; } = "root";

    /// <summary>
    /// Maximum simultaneous in-flight HTTP requests against a single
    /// connection pool. Default 32 matches the spec-stated default and
    /// SurrealDB single-node practical concurrency ceiling.
    /// </summary>
    public int MaxConnections { get; set; } = 32;

    /// <summary>Per-request HTTP timeout. Default 30s.</summary>
    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Maximum retries on transport failure. Default 5.</summary>
    public int MaxRetries { get; set; } = 5;

    /// <summary>Base backoff before applying jitter + exponential factor. Default 100ms.</summary>
    public TimeSpan BaseRetryDelay { get; set; } = TimeSpan.FromMilliseconds(100);

    /// <summary>Jitter ratio (0..1) applied to each backoff. Default 0.5 = ±50%.</summary>
    public double JitterRatio { get; set; } = 0.5;
}
