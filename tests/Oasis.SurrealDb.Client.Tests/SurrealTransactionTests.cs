using System.Threading.Tasks;
using FluentAssertions;
using Oasis.SurrealDb.Client.Connection;

namespace Oasis.SurrealDb.Client.Tests;

/// <summary>
/// Closes negative-space G-C: COMMIT on explicit commit, CANCEL on
/// dispose-without-commit, and idempotent commit.
/// </summary>
public class SurrealTransactionTests
{
    private static SurrealConnectionOptions Opts() => new()
    {
        Endpoint   = "http://localhost:8442",
        MaxRetries = 1,
    };

    private const string OkBody = """[ { "status": "OK", "time": "1µs", "result": null } ]""";

    [Fact]
    public async Task CommitPath_EmitsBeginThenCommit_NoCancel()
    {
        var handler = new FakeHttpHandler();
        handler.EnqueueOk(OkBody); // BEGIN
        handler.EnqueueOk(OkBody); // user statement
        handler.EnqueueOk(OkBody); // COMMIT

        await using var conn = new HttpSurrealConnection(handler, Opts());

        await using (var txn = await conn.BeginTransactionAsync())
        {
            await conn.ExecuteRawAsync("UPDATE wallet:abc SET amount = 100;");
            await txn.CommitAsync();
            txn.IsCommitted.Should().BeTrue();
        }

        handler.Requests.Should().HaveCount(3);
        handler.Requests[0].Body.Should().Be("BEGIN TRANSACTION;");
        handler.Requests[1].Body.Should().StartWith("UPDATE wallet:abc");
        handler.Requests[2].Body.Should().Be("COMMIT TRANSACTION;");
    }

    [Fact]
    public async Task DisposeWithoutCommit_EmitsCancel()
    {
        var handler = new FakeHttpHandler();
        handler.EnqueueOk(OkBody); // BEGIN
        handler.EnqueueOk(OkBody); // CANCEL on dispose

        await using var conn = new HttpSurrealConnection(handler, Opts());

        await using (var txn = await conn.BeginTransactionAsync())
        {
            // No CommitAsync — dispose path should send CANCEL.
            txn.IsCommitted.Should().BeFalse();
        }

        handler.Requests.Should().HaveCount(2);
        handler.Requests[0].Body.Should().Be("BEGIN TRANSACTION;");
        handler.Requests[1].Body.Should().Be("CANCEL TRANSACTION;");
    }

    [Fact]
    public async Task CommitAsync_Idempotent_SecondCallIsNoOp()
    {
        var handler = new FakeHttpHandler();
        handler.EnqueueOk(OkBody); // BEGIN
        handler.EnqueueOk(OkBody); // COMMIT (first)

        await using var conn = new HttpSurrealConnection(handler, Opts());
        var txn = await conn.BeginTransactionAsync();
        await txn.CommitAsync();
        await txn.CommitAsync(); // no-op — must NOT send a second COMMIT

        await txn.DisposeAsync();

        handler.Requests.Should().HaveCount(2,
            "second CommitAsync and dispose-after-commit must not emit further statements");
    }
}
