using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Moyai.Application.Authentication;
using Moyai.Infrastructure.Authentication;
using Moyai.Infrastructure.Providers;

/// <summary>専用fixtureの読取と認証拒否だけを検査します。Providerを起動・変更しません。</summary>
internal static class InteropChecks
{
    internal sealed record Check(string Name, bool Passed, string? Code = null);

    internal static async Task<IReadOnlyList<Check>> VerifyAsync(McpRepositoryProviderOptions options,
        FixturePlan plan, IAssertionIssuer issuer, CancellationToken cancellationToken)
    {
        var checks = new List<Check>();
        var factory = new FixtureHttpClientFactory();
        using (var owner = new ProviderConnection(options, factory))
        {
            await using var transport = new HttpClientTransport(new HttpClientTransportOptions
            { Endpoint = owner.Endpoint, TransportMode = HttpTransportMode.StreamableHttp }, owner.Client);
            await using McpClient client = await McpClient.CreateAsync(transport, cancellationToken: cancellationToken);
            var tools = await client.ListToolsAsync(cancellationToken: cancellationToken);
            checks.Add(new("owner_initialize_and_tools_list", tools.Any(tool => tool.Name == "bitbucket_repository_status")));
            CallToolResult noAssertion = await client.CallToolAsync("bitbucket_repository_status",
                new Dictionary<string, object?> { ["repository"] = plan.RepositoryId }, cancellationToken: cancellationToken);
            checks.Add(new("owner_native_direct_read_succeeds", ReadSucceeded(noAssertion)));
        }
        using (var owner = new ProviderConnection(options, factory))
        {
            var context = new AssertionContext("buckettie", plan.ProjectId, RepositoryAssertionPolicy.NormalizeRepository(plan.RepositoryUrl),
                "bitbucket_repository_status", ["repository.push"], Guid.NewGuid().ToString("N"));
            using var handler = new AssertionHttpHandler(owner.Client, issuer, context);
            using var http = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
            await using var transport = new HttpClientTransport(new HttpClientTransportOptions
            { Endpoint = owner.Endpoint, TransportMode = HttpTransportMode.StreamableHttp }, http);
            await using McpClient client = await McpClient.CreateAsync(transport, cancellationToken: cancellationToken);
            CallToolResult wrongScope = await client.CallToolAsync("bitbucket_repository_status",
                new Dictionary<string, object?> { ["repository"] = plan.RepositoryId }, cancellationToken: cancellationToken);
            string? code = ReadError(wrongScope);
            checks.Add(new("wrong_scope_signed_read_rejected", code is "auth_scope_denied" or "auth_scope_mismatch", SafeCode(code)));
        }
        using (UserHostConnection resolved = BuckettieUserHostResolver.Resolve(options.RuntimeDirectory!, options.ExpectedMcpPath))
        {
            var context = new AssertionContext("buckettie", plan.ProjectId, RepositoryAssertionPolicy.NormalizeRepository(plan.RepositoryUrl),
                "bitbucket_repository_status", ["repository.read"], Guid.NewGuid().ToString("N"));
            SignedAssertion assertion = await issuer.IssueAsync(context, cancellationToken);
            using HttpClient direct = factory.CreateClient(options.Name);
            checks.Add(await OwnerGateAsync(direct, resolved.Endpoint, plan.RepositoryId, null, assertion, context.OperationId,
                "assertion_only_rejected", "user_host_credential_required", cancellationToken));
            string incorrectCredential = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
            checks.Add(await OwnerGateAsync(direct, resolved.Endpoint, plan.RepositoryId, incorrectCredential, assertion, context.OperationId,
                "incorrect_owner_rejected", "user_host_credential_invalid", cancellationToken));
        }
        return checks;
    }

    internal static async Task<Check> RotationAsync(McpRepositoryProviderOptions options, CancellationToken cancellationToken)
    {
        using UserHostConnection before = BuckettieUserHostResolver.Resolve(options.RuntimeDirectory!, options.ExpectedMcpPath);
        // No credential or derivative is written to disk. The Provider owner coordinates restart separately.
        Console.WriteLine("{\"stage\":\"provider_restart_required\",\"normalHostContacted\":false}");
        string? confirmation = await Console.In.ReadLineAsync(cancellationToken);
        if (confirmation != "provider-ready") return new("restart_generation_and_credential_rotated", false, "restart_confirmation_required");
        using UserHostConnection after = BuckettieUserHostResolver.Resolve(options.RuntimeDirectory!, options.ExpectedMcpPath);
        bool changed = before.Generation != after.Generation && before.Credential != after.Credential;
        return new("restart_generation_and_credential_rotated", changed, changed ? null : "generation_or_credential_unchanged");
    }

    private static async Task<Check> OwnerGateAsync(HttpClient http, Uri endpoint, string repository,
        string? credential, SignedAssertion assertion, string operationId, string name, string expected, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
        request.Headers.Accept.ParseAdd("application/json");
        request.Headers.Accept.ParseAdd("text/event-stream");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", assertion.Value);
        request.Headers.Add("X-Moyai-Operation-Id", operationId);
        if (credential is not null) request.Headers.Add("X-Buckettie-Session", credential);
        request.Content = new StringContent(JsonSerializer.Serialize(new
        { jsonrpc = "2.0", id = 1, method = "tools/call", @params = new { name = "bitbucket_repository_status", arguments = new { repository } } }), Encoding.UTF8, "application/json");
        try
        {
            using HttpResponseMessage response = await http.SendAsync(request, cancellationToken);
            string payload = await response.Content.ReadAsStringAsync(cancellationToken);
            bool rejected = response.StatusCode == HttpStatusCode.Unauthorized
                && ProviderConnection.IsOwnerRejection(payload, response.StatusCode);
            string? code = null;
            if (rejected)
            {
                using JsonDocument document = JsonDocument.Parse(payload);
                code = document.RootElement.GetProperty("error").GetProperty("code").GetString();
            }
            return new(name, rejected && code == expected, rejected ? expected : "unexpected_owner_gate_response");
        }
        finally
        {
            request.Headers.Authorization = null;
            request.Headers.Remove("X-Buckettie-Session");
        }
    }

    private static bool ReadSucceeded(CallToolResult result)
    {
        if (result.IsError == true) return false;
        string? text = result.StructuredContent?.GetRawText() ?? result.Content.OfType<TextContentBlock>().FirstOrDefault()?.Text;
        if (string.IsNullOrEmpty(text)) return false;
        using JsonDocument document = JsonDocument.Parse(text);
        return document.RootElement.ValueKind == JsonValueKind.Object
            && document.RootElement.TryGetProperty("ok", out JsonElement ok) && ok.ValueKind == JsonValueKind.True;
    }

    private static string? ReadError(CallToolResult result)
    {
        string? text = result.StructuredContent?.GetRawText() ?? result.Content.OfType<TextContentBlock>().FirstOrDefault()?.Text;
        if (string.IsNullOrEmpty(text)) return null;
        using JsonDocument document = JsonDocument.Parse(text);
        JsonElement root = document.RootElement;
        return root.TryGetProperty("ok", out JsonElement ok) && ok.ValueKind == JsonValueKind.False
            && root.TryGetProperty("error", out JsonElement error) && error.ValueKind == JsonValueKind.Object
            && error.TryGetProperty("code", out JsonElement code) && code.ValueKind == JsonValueKind.String ? code.GetString() : null;
    }

    private static string SafeCode(string? code) => code is "auth_project_mismatch" or "auth_assertion_invalid" or "auth_assertion_required"
        or "auth_required" or "auth_scope_denied" or "auth_scope_mismatch" ? code : "unexpected_authentication_response";
}
