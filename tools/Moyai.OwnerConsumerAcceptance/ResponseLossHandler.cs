using System.Text.Json;
using Moyai.Infrastructure.Providers;

namespace Moyai.OwnerConsumerAcceptance;

/// <summary>Discards one completed mutation response in the dedicated acceptance process.</summary>
internal sealed class ResponseLossHandler : DelegatingHandler
{
    private readonly string _tool;
    private int _attempts;
    private int _dispatches;
    private int _discarded;
    internal int MutationDispatches => Volatile.Read(ref _dispatches);
    internal int ResponsesDiscarded => Volatile.Read(ref _discarded);

    internal ResponseLossHandler(string tool, HttpMessageHandler inner) : base(inner)
    {
        if (tool is not ("bitbucket_repository_commit" or "bitbucket_repository_commit_selected"))
            throw new ArgumentException("Only an explicit local commit tool is supported.", nameof(tool));
        _tool = tool;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        string? tool = null;
        if (request.Content is not null)
        {
            using JsonDocument document = JsonDocument.Parse(await request.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false));
            JsonElement root = document.RootElement;
            if (root.TryGetProperty("method", out JsonElement method) && method.GetString() == "tools/call")
                tool = root.GetProperty("params").GetProperty("name").GetString();
        }
        if (tool != _tool)
        {
            if (tool is not null && tool != "get_version" && !WindowsOwnerContract.IsRead(tool))
                throw new InvalidOperationException("An unapproved mutation was blocked by the acceptance harness.");
            return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
        if (Interlocked.Increment(ref _attempts) != 1)
            throw new InvalidOperationException("A second mutation was blocked by the acceptance harness.");
        Interlocked.Increment(ref _dispatches);
        HttpResponseMessage response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) return response;
        using (response)
        {
            await response.Content.LoadIntoBufferAsync(1024 * 1024).WaitAsync(cancellationToken).ConfigureAwait(false);
            Interlocked.Increment(ref _discarded);
        }
        throw new HttpRequestException("Acceptance fixture response intentionally withheld.");
    }
}
