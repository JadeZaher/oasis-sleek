using System;
using System.Threading;
using System.Threading.Tasks;

namespace Oasis.SurrealDb.Client.Transaction;

/// <summary>
/// Explicit SurrealDB transaction handle. Constructed via
/// <see cref="Connection.ISurrealConnection.BeginTransactionAsync"/>, which
/// has already issued <c>BEGIN TRANSACTION;</c> on the underlying
/// connection. Callers MUST <c>await using</c> the handle: a successful
/// <see cref="CommitAsync"/> sends <c>COMMIT TRANSACTION;</c>; otherwise
/// <see cref="IAsyncDisposable.DisposeAsync"/> sends <c>CANCEL TRANSACTION;</c>.
/// This is the shape that closes negative-space G-C: there is no path that
/// leaves a transaction open after the using block exits.
/// </summary>
public interface ISurrealTransaction : IAsyncDisposable
{
    /// <summary>True iff <see cref="CommitAsync"/> has completed without error.</summary>
    bool IsCommitted { get; }

    /// <summary>True iff <see cref="DisposeAsync"/> has been entered.</summary>
    bool IsDisposed { get; }

    /// <summary>
    /// Send <c>COMMIT TRANSACTION;</c>. Idempotent: subsequent calls are
    /// no-ops, so the natural "Commit then dispose-without-cancel" pattern
    /// does the right thing. Throws if the connection itself faults.
    /// </summary>
    Task CommitAsync(CancellationToken ct = default);
}
