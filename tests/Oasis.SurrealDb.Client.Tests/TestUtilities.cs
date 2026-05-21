using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Oasis.SurrealDb.Client.Tests;

/// <summary>
/// Recordable fake <see cref="HttpMessageHandler"/>. Records every request
/// (URI, method, request body, headers) and replies from a scripted queue
/// of <see cref="HttpResponseMessage"/>. Unit tests run against this — no
/// live SurrealDB container needed.
/// </summary>
internal sealed class FakeHttpHandler : HttpMessageHandler
{
    private readonly Queue<Func<HttpRequestMessage, Task<HttpResponseMessage>>> _responses = new();
    public List<RecordedRequest> Requests { get; } = new();

    public void EnqueueOk(string jsonBody) =>
        Enqueue(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(jsonBody, System.Text.Encoding.UTF8, "application/json"),
        }));

    public void Enqueue(Func<HttpRequestMessage, Task<HttpResponseMessage>> handler)
    {
        _responses.Enqueue(handler);
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync();
        Requests.Add(new RecordedRequest(
            Method: request.Method.Method,
            Uri:    request.RequestUri?.ToString() ?? string.Empty,
            Body:   body,
            NsHeader: TryGetHeader(request, "NS"),
            DbHeader: TryGetHeader(request, "DB"),
            HasAuth: request.Headers.Authorization is not null));

        if (_responses.Count == 0)
        {
            throw new InvalidOperationException(
                "FakeHttpHandler ran out of scripted responses; recorded " + Requests.Count + " request(s).");
        }
        return await _responses.Dequeue()(request);
    }

    private static string? TryGetHeader(HttpRequestMessage req, string name) =>
        req.Headers.TryGetValues(name, out var vals) ? string.Join(",", vals) : null;

    internal sealed record RecordedRequest(
        string Method,
        string Uri,
        string? Body,
        string? NsHeader,
        string? DbHeader,
        bool HasAuth);
}
