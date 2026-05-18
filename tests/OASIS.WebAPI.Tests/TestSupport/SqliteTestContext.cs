using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using OASIS.WebAPI.Data;
using OASIS.WebAPI.Models.Responses;
using OASIS.WebAPI.Models.Sagas;

namespace OASIS.WebAPI.Tests.TestSupport;

/// <summary>
/// <see cref="OASISDbContext"/> usable on SQLite: it calls
/// <c>base.OnModelCreating</c> FIRST (every key + every UNIQUE/filtered index
/// inherited unchanged — those are exactly what the safety tests exercise) then
/// remaps only the <c>xmin</c>-mapped concurrency tokens
/// (<see cref="BridgeTransactionResult.Version"/> and
/// <see cref="SagaStepRecord.Version"/>). xmin has no SQLite equivalent
/// (PostgreSQL system column / xid type) so each becomes a plain
/// non-store-generated, non-concurrency INTEGER. Nothing else is altered — the
/// saga conditional-claim/lease semantics under test rely on the
/// <c>ExecuteUpdateAsync … WHERE Status==…</c> predicate, NOT this token, so
/// the remap does not weaken the proof (identical to the bridge row).
/// </summary>
public sealed class SqliteTestDbContext : OASISDbContext
{
    public SqliteTestDbContext(DbContextOptions<OASISDbContext> options) : base(options) { }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<BridgeTransactionResult>(e =>
        {
            e.Property(b => b.Version)
             .HasColumnName("Version")
             .HasColumnType("INTEGER")
             .ValueGeneratedNever()
             .IsConcurrencyToken(false);
        });

        modelBuilder.Entity<SagaStepRecord>(e =>
        {
            e.Property(s => s.Version)
             .HasColumnName("Version")
             .HasColumnType("INTEGER")
             .ValueGeneratedNever()
             .IsConcurrencyToken(false);
        });
    }
}

/// <summary>
/// Owns one SQLite database for a test's lifetime, holding a kept-open
/// connection so the database survives between the independent per-context
/// connections each <see cref="NewContext"/> opens (modelling concurrent scoped
/// requests against one DB).
///
/// Two backing modes — pick the one the test's invariant requires:
///
/// <para><see cref="SharedCacheInMemory"/>: a uniquely-named
/// <c>Mode=Memory;Cache=Shared</c> DB. Independent connections attach to the
/// SAME physical in-memory DB (one real UNIQUE constraint). Fast; the right
/// choice when the proof is "the relational UNIQUE constraint is genuinely
/// shared across contexts" (idempotency / reconciliation conditional-update
/// arbitration). A private <c>:memory:</c> per connection would give each its
/// own DB and make the shared-constraint tests meaningless.</para>
///
/// <para><see cref="FileBacked"/>: an on-disk temp file with
/// <c>Pooling=False;Default Timeout=30</c> + WAL. A real file DB takes SQLite's
/// database-level write lock with a busy_timeout, so the SELECT→INSERT in
/// IdempotencyStore.TryClaimAsync and the conditional UPDATE … WHERE
/// Status==VAAReady are GENUINELY serialized across separate per-request
/// connections. Shared-cache in-memory only takes table-level locks dropped
/// between statements, which under CPU contention lets writers slip through the
/// TOCTOU window and produces a FALSE concurrency failure even though
/// production (PostgreSQL row-level locks + the UNIQUE constraint) is correct.
/// The file engine is strictly MORE faithful to production; no safety guarantee
/// is weakened, the exactly-once arbitration is exercised harder. The bridge
/// concurrency test depends on this mode — do not swap it.</para>
/// </summary>
public sealed class SqliteTestContext : IDisposable
{
    private readonly SqliteConnection _keepAlive;
    private readonly DbContextOptions<OASISDbContext> _options;
    private readonly string? _dbPath;

    private SqliteTestContext(string connectionString, string? dbPath)
    {
        _dbPath = dbPath;
        _keepAlive = new SqliteConnection(connectionString);
        _keepAlive.Open();

        if (dbPath is not null)
        {
            // WAL: readers don't block the writer; the single writer is still
            // fully serialized — the conditional-UPDATE/UNIQUE arbitration is
            // unchanged, contention just no longer manifests as a flaky failure.
            using var pragma = _keepAlive.CreateCommand();
            pragma.CommandText = "PRAGMA journal_mode=WAL;";
            pragma.ExecuteNonQuery();
        }

        _options = new DbContextOptionsBuilder<OASISDbContext>()
            .UseSqlite(connectionString)
            .Options;

        using var ctx = NewContext();
        ctx.Database.EnsureCreated();
    }

    /// <summary>Uniquely-named shared-cache in-memory DB (see class remarks).</summary>
    public static SqliteTestContext SharedCacheInMemory()
    {
        var dbName = $"sqlitetest_{Guid.NewGuid():N}";
        return new SqliteTestContext(
            $"DataSource={dbName};Mode=Memory;Cache=Shared", dbPath: null);
    }

    /// <summary>On-disk temp file with real DB-level write locking + WAL (see
    /// class remarks). Required by the bridge concurrency test.</summary>
    public static SqliteTestContext FileBacked()
    {
        var dbPath = Path.Combine(
            Path.GetTempPath(), $"sqlitetest_{Guid.NewGuid():N}.db");
        // Pooling=False → each context gets its own physical connection (genuine
        // concurrency, not a pooled shared one). Default Timeout=30 → contended
        // writers busy-wait for the real DB lock instead of failing "database is
        // locked", keeping the test deterministic WITHOUT weakening the
        // exactly-once arbitration (the lock + UNIQUE constraints still elect).
        return new SqliteTestContext(
            $"Data Source={dbPath};Pooling=False;Default Timeout=30", dbPath);
    }

    /// <summary>A fresh context with its OWN connection to the shared DB —
    /// independent instances model independent scoped DbContexts.</summary>
    public OASISDbContext NewContext() => new SqliteTestDbContext(_options);

    public void Dispose()
    {
        _keepAlive.Dispose();
        if (_dbPath is null)
            return;

        // Pooling=False means no connections linger; clear pools defensively
        // then remove the temp DB file (+ WAL/SHM sidecars).
        SqliteConnection.ClearAllPools();
        foreach (var path in new[] { _dbPath, _dbPath + "-wal", _dbPath + "-shm" })
        {
            try { if (File.Exists(path)) File.Delete(path); }
            catch { /* best-effort temp cleanup */ }
        }
    }
}
