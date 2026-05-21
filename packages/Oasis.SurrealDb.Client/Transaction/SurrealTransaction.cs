using System;
using System.Threading;
using System.Threading.Tasks;
using Oasis.SurrealDb.Client.Connection;

namespace Oasis.SurrealDb.Client.Transaction;

/// <summary>
/// Default <see cref="ISurrealTransaction"/> implementation. The constructor
/// is internal — callers obtain instances exclusively through
/// <see cref="ISurrealConnection.BeginTransactionAsync"/>.
/// </summary>
public sealed class SurrealTransaction : ISurrealTransaction
{
    private readonly ISurrealConnection _connection;
    private int _committed; // 0 = not committed, 1 = committed
    private int _disposed;  // 0 = live,           1 = dispose entered

    /// <inheritdoc/>
    public bool IsCommitted => Volatile.Read(ref _committed) == 1;

    /// <inheritdoc/>
    public bool IsDisposed  => Volatile.Read(ref _disposed)  == 1;

    internal SurrealTransaction(ISurrealConnection connection)
    {
        _connection = connection ?? throw new ArgumentNullException(nameof(connection));
    }

    /// <summary>
    /// Factory helper used by connections. Sends <c>BEGIN TRANSACTION;</c>
    /// before returning the handle so the caller's first <c>ExecuteRawAsync</c>
    /// is already inside the txn.
    /// </summary>
    public static async Task<SurrealTransaction> StartAsync(ISurrealConnection connection, CancellationToken ct)
    {
        if (connection is null) throw new ArgumentNullException(nameof(connection));
        var resp = await connection.ExecuteRawAsync("BEGIN TRANSACTION;", null, ct).ConfigureAwait(false);
        resp.EnsureAllOk();
        return new SurrealTransaction(connection);
    }

    /// <inheritdoc/>
    public async Task CommitAsync(CancellationToken ct = default)
    {
        // Idempotent — already committed is a no-op, not an error.
        if (Interlocked.CompareExchange(ref _committed, 1, 0) != 0) return;
        var resp = await _connection.ExecuteRawAsync("COMMIT TRANSACTION;", null, ct).ConfigureAwait(false);
        resp.EnsureAllOk();
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.CompareExchange(ref _disposed, 1, 0) != 0) return;
        if (IsCommitted) return;

        // CANCEL TRANSACTION on the unhappy path. Swallow exceptions here —
        // we are already on the unwind path and the connection may itself
        // be faulted; the contract is "best-effort cancel on dispose."
        try
        {
            var resp = await _connection.ExecuteRawAsync("CANCEL TRANSACTION;", null, CancellationToken.None)
                                        .ConfigureAwait(false);
            // Intentionally NOT EnsureAllOk — cancel-on-faulted-connection is
            // an expected unhappy path and must not throw from dispose.
            _ = resp;
        }
        catch
        {
            // Best-effort: swallow on dispose. The original exception (if any)
            // surfaces through the using statement's primary control flow.
        }
    }
}
