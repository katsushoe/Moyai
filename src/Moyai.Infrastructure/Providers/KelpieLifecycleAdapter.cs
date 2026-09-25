using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Moyai.Application.Authentication;
using Moyai.Application.Lifecycle;
using Moyai.Infrastructure.Authentication;

namespace Moyai.Infrastructure.Providers;

/// <summary>KelpieSSH Protocol v2の段階Deployを統括します。</summary>
internal sealed class KelpieLifecycleAdapter
{
    private const string Audience = "kelpiessh";
    private const string ResourceKind = "kelpie_target";
    private static readonly Dictionary<string, string[]> ToolScopes = new(StringComparer.Ordinal)
    {
        ["target_status"] = ["target.status"],
        ["deploy_prepare"] = ["deploy.prepare"],
        ["deploy_upload"] = ["deploy.upload"],
        ["deploy_activate"] = ["deploy.activate"],
        ["deploy_verify"] = ["deploy.verify"],
        ["deploy_rollback"] = ["deploy.rollback"],
        ["deploy_cleanup"] = ["deploy.cleanup"],
        ["deploy_status"] = ["deploy.status"],
    };

    private readonly McpRepositoryProviderOptions _options;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IAssertionIssuer? _issuer;
    private readonly IAssertionAudit? _audit;

    public KelpieLifecycleAdapter(
        McpRepositoryProviderOptions options,
        IHttpClientFactory httpClientFactory,
        IAssertionIssuer? issuer,
        IAssertionAudit? audit)
    {
        _options = options;
        _httpClientFactory = httpClientFactory;
        _issuer = issuer;
        _audit = audit;
    }

    public async Task<LifecycleResult> ExecuteAsync(LifecycleRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        string operation = request.Action == LifecycleAction.Deploy ? "deploy" : "deploy_rollback";
        try
        {
            ValidateRequest(request);
            return request.Action == LifecycleAction.Deploy
                ? await DeployAsync(request, cancellationToken).ConfigureAwait(false)
                : await RollbackAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (ProviderAuthenticationException exception)
        {
            return new LifecycleResult(false, operation, null, exception.Code, exception.Code);
        }
    }

    private async Task<LifecycleResult> DeployAsync(LifecycleRequest request, CancellationToken cancellationToken)
    {
        string deploymentId = request.DeploymentId!.Value.ToString("D");
        string target = request.KelpieTarget!;
        KelpieCallResult targetStatus = await CallAsync(
            request,
            "target_status",
            new Dictionary<string, object?> { ["targetName"] = target, ["targetId"] = target },
            cancellationToken).ConfigureAwait(false);
        if (!targetStatus.Ok)
        {
            return Failure("deploy", targetStatus);
        }

        var stages = new[]
        {
            new KelpieStage("deploy_prepare", "prepared", new Dictionary<string, object?>
            {
                ["deploymentId"] = deploymentId,
                ["targetName"] = target,
                ["targetId"] = target,
                ["destination"] = request.DestinationPath,
            }),
            new KelpieStage("deploy_upload", "uploaded", new Dictionary<string, object?>
            {
                ["deploymentId"] = deploymentId,
                ["artifactPath"] = request.ArtifactPath,
                ["sha256"] = request.ArtifactSha256,
            }),
            new KelpieStage("deploy_activate", "activated", DeploymentArgument(deploymentId)),
            new KelpieStage("deploy_verify", "verified", DeploymentArgument(deploymentId)),
            new KelpieStage("deploy_cleanup", "cleaned", DeploymentArgument(deploymentId)),
        };

        KelpieCallResult? latest = null;
        foreach (KelpieStage stage in stages)
        {
            latest = await CallAsync(request, stage.Tool, stage.Arguments, cancellationToken).ConfigureAwait(false);
            if (latest.OutcomeUnknown)
            {
                KelpieCallResult status = await CallAsync(
                    request,
                    "deploy_status",
                    DeploymentArgument(deploymentId),
                    cancellationToken).ConfigureAwait(false);
                if (!status.Ok || !StateSatisfies(status.State, stage.ExpectedState))
                {
                    return Unknown("deploy");
                }

                latest = status;
                continue;
            }

            if (!latest.Ok)
            {
                return Failure("deploy", latest);
            }

            if (!StateSatisfies(latest.State, stage.ExpectedState))
            {
                return Invalid("deploy", $"{stage.Tool} returned an unexpected deployment state.");
            }
        }

        return new LifecycleResult(true, "deploy", latest?.Output, null, null);
    }

    private async Task<LifecycleResult> RollbackAsync(LifecycleRequest request, CancellationToken cancellationToken)
    {
        string deploymentId = request.DeploymentId!.Value.ToString("D");
        KelpieCallResult rollback = await CallAsync(
            request,
            "deploy_rollback",
            DeploymentArgument(deploymentId),
            cancellationToken).ConfigureAwait(false);
        if (rollback.OutcomeUnknown)
        {
            KelpieCallResult status = await CallAsync(
                request,
                "deploy_status",
                DeploymentArgument(deploymentId),
                cancellationToken).ConfigureAwait(false);
            if (!status.Ok || !StateSatisfies(status.State, "rolled_back"))
            {
                return Unknown("deploy_rollback");
            }
        }
        else if (!rollback.Ok)
        {
            return Failure("deploy_rollback", rollback);
        }
        else if (!StateSatisfies(rollback.State, "rolled_back"))
        {
            return Invalid("deploy_rollback", "deploy_rollback returned an unexpected deployment state.");
        }

        KelpieCallResult cleanup = await CallAsync(
            request,
            "deploy_cleanup",
            DeploymentArgument(deploymentId),
            cancellationToken).ConfigureAwait(false);
        if (cleanup.OutcomeUnknown)
        {
            KelpieCallResult status = await CallAsync(
                request,
                "deploy_status",
                DeploymentArgument(deploymentId),
                cancellationToken).ConfigureAwait(false);
            return status.Ok && StateSatisfies(status.State, "cleaned")
                ? new LifecycleResult(true, "deploy_rollback", status.Output, null, null)
                : Unknown("deploy_rollback");
        }

        if (!cleanup.Ok)
        {
            return Failure("deploy_rollback", cleanup);
        }

        return StateSatisfies(cleanup.State, "cleaned")
            ? new LifecycleResult(true, "deploy_rollback", cleanup.Output, null, null)
            : Invalid("deploy_rollback", "deploy_cleanup returned an unexpected deployment state.");
    }

    private async Task<KelpieCallResult> CallAsync(
        LifecycleRequest request,
        string tool,
        IReadOnlyDictionary<string, object?> arguments,
        CancellationToken cancellationToken)
    {
        KelpieCallResult result = await CallOnceAsync(request, tool, arguments, cancellationToken).ConfigureAwait(false);
        if (string.Equals(result.ErrorCode, "auth_assertion_expired", StringComparison.Ordinal))
        {
            result = await CallOnceAsync(request, tool, arguments, cancellationToken).ConfigureAwait(false);
        }

        return result;
    }

    private async Task<KelpieCallResult> CallOnceAsync(
        LifecycleRequest request,
        string tool,
        IReadOnlyDictionary<string, object?> arguments,
        CancellationToken cancellationToken)
    {
        if (_issuer is null)
        {
            throw new ProviderAuthenticationException("authentication_unavailable");
        }

        AssertionContext context = Context(request, tool);
        var capability = new AssertionCapability(
            Audience,
            Audience,
            "2",
            "ES256",
            true,
            ToolScopes,
            [ResourceKind]);
        capability.Require(context);
        try
        {
            var transportOptions = new HttpClientTransportOptions
            {
                Endpoint = _options.Endpoint,
                TransportMode = HttpTransportMode.StreamableHttp,
                AdditionalHeaders = new Dictionary<string, string>(),
            };
            using HttpClient providerClient = _httpClientFactory.CreateClient(_options.Name);
            using var assertionHandler = new AssertionHttpHandler(providerClient, _issuer, context, _audit);
            using var httpClient = new HttpClient(assertionHandler);
            await using var transport = new HttpClientTransport(transportOptions, httpClient);
            await using McpClient client = await McpClient.CreateAsync(
                transport,
                cancellationToken: cancellationToken).ConfigureAwait(false);
            CallToolResult response = await client.CallToolAsync(
                tool,
                arguments,
                cancellationToken: cancellationToken).ConfigureAwait(false);
            KelpieCallResult parsed = KelpieCallResult.Parse(tool, response);
            parsed = parsed with
            {
                Output = assertionHandler.Redact(parsed.Output),
                ErrorMessage = assertionHandler.Redact(parsed.ErrorMessage),
            };
            if (_audit is not null)
            {
                await _audit.WriteAsync(
                    context,
                    assertionHandler.KeyId,
                    parsed.Ok ? "accepted" : parsed.ErrorCode ?? "provider_operation_failed",
                    cancellationToken).ConfigureAwait(false);
            }

            return parsed;
        }
        catch (Exception exception) when (exception is HttpRequestException or TimeoutException or ModelContextProtocol.McpException)
        {
            return KelpieCallResult.Unknown;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return KelpieCallResult.Unknown;
        }
    }

    private static AssertionContext Context(LifecycleRequest request, string tool)
    {
        string resource = request.KelpieTarget!;
        return new AssertionContext(
            Audience,
            request.ProjectId!.Value,
            null,
            tool,
            ToolScopes[tool],
            $"{request.DeploymentId!.Value:D}:{tool}",
            "2",
            ResourceKind,
            resource);
    }

    private static void ValidateRequest(LifecycleRequest request)
    {
        if (request.Action is not (LifecycleAction.Deploy or LifecycleAction.DeployRollback)
            || !request.ProjectId.HasValue
            || request.ProjectId.Value == Guid.Empty
            || !request.DeploymentId.HasValue
            || request.DeploymentId.Value == Guid.Empty
            || string.IsNullOrWhiteSpace(request.KelpieTarget))
        {
            throw new ProviderAuthenticationException("auth_project_mismatch");
        }

        if (request.Action == LifecycleAction.Deploy
            && (string.IsNullOrWhiteSpace(request.ArtifactPath)
                || string.IsNullOrWhiteSpace(request.DestinationPath)
                || !IsSha256(request.ArtifactSha256)))
        {
            throw new ProviderAuthenticationException("auth_project_mismatch");
        }
    }

    private static bool IsSha256(string? value) => value is { Length: 64 }
        && value.All(static character => char.IsAsciiHexDigit(character));

    private static Dictionary<string, object?> DeploymentArgument(string deploymentId) => new()
    {
        ["deploymentId"] = deploymentId,
    };

    private static bool StateSatisfies(string? actual, string expected) => expected switch
    {
        "prepared" => actual is "prepared" or "uploaded" or "activated" or "verified" or "cleaned",
        "uploaded" => actual is "uploaded" or "activated" or "verified" or "cleaned",
        "activated" => actual is "activated" or "verified" or "cleaned",
        "verified" => actual is "verified" or "cleaned",
        "rolled_back" => actual is "rolled_back" or "cleaned",
        "cleaned" => actual == "cleaned",
        _ => false,
    };

    private static LifecycleResult Failure(string operation, KelpieCallResult result) =>
        new(false, operation, result.Output, result.ErrorCode ?? "provider_operation_failed", result.ErrorMessage);

    private static LifecycleResult Invalid(string operation, string detail) =>
        new(false, operation, null, "provider_invalid_response", detail);

    private static LifecycleResult Unknown(string operation) =>
        new(false, operation, null, "provider_operation_unknown", "Provider transport failed; operation outcome remains unknown.");

    private sealed record KelpieStage(
        string Tool,
        string ExpectedState,
        IReadOnlyDictionary<string, object?> Arguments);

    private sealed record KelpieCallResult(
        bool Ok,
        string? Output,
        string? ErrorCode,
        string? ErrorMessage,
        string? State,
        bool OutcomeUnknown)
    {
        public static KelpieCallResult Unknown { get; } = new(
            false,
            null,
            "provider_operation_unknown",
            "Provider transport failed; operation outcome remains unknown.",
            null,
            true);

        public static KelpieCallResult Parse(string tool, CallToolResult result)
        {
            string? text = result.Content.OfType<TextContentBlock>().FirstOrDefault()?.Text;
            string? output = result.StructuredContent?.GetRawText() ?? text;
            if (string.IsNullOrWhiteSpace(output))
            {
                return Invalid(output, "Provider returned no JSON result.");
            }

            try
            {
                using JsonDocument document = JsonDocument.Parse(output);
                JsonElement root = document.RootElement;
                if (string.Equals(tool, "target_status", StringComparison.Ordinal))
                {
                    if (!TryProperty(root, "available", out JsonElement available)
                        || available.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                    {
                        return Invalid(output, "target_status must contain available.");
                    }

                    return available.GetBoolean()
                        ? new(true, output, null, null, null, false)
                        : new(false, output, "target_not_found", "KelpieSSH target is unavailable.", null, false);
                }

                if (!TryBoolean(root, "success", out bool success) && !TryBoolean(root, "ok", out success))
                {
                    return Invalid(output, "Deployment result must contain success.");
                }

                JsonElement deployment = TryProperty(root, "deployment", out JsonElement foundDeployment)
                    ? foundDeployment
                    : root;
                string? state = ReadState(deployment);
                if (success)
                {
                    return new(true, output, null, null, state, false);
                }

                JsonElement error = TryProperty(deployment, "error", out JsonElement nestedError)
                    ? nestedError
                    : TryProperty(root, "error", out JsonElement rootError) ? rootError : default;
                string? code = error.ValueKind == JsonValueKind.Object && TryProperty(error, "code", out JsonElement codeElement)
                    ? codeElement.GetString()?.Replace('-', '_')
                    : null;
                string? message = error.ValueKind == JsonValueKind.Object && TryProperty(error, "message", out JsonElement messageElement)
                    ? messageElement.GetString()
                    : null;
                return new(false, output, code ?? "provider_operation_failed", message ?? "Provider operation failed.", state, false);
            }
            catch (Exception exception) when (exception is JsonException or InvalidOperationException)
            {
                return Invalid(output, exception.Message);
            }
        }

        private static KelpieCallResult Invalid(string? output, string detail) =>
            new(false, output, "provider_invalid_response", detail, null, false);

        private static bool TryBoolean(JsonElement element, string name, out bool value)
        {
            if (TryProperty(element, name, out JsonElement property)
                && property.ValueKind is JsonValueKind.True or JsonValueKind.False)
            {
                value = property.GetBoolean();
                return true;
            }

            value = false;
            return false;
        }

        private static bool TryProperty(JsonElement element, string name, out JsonElement value)
        {
            if (element.ValueKind == JsonValueKind.Object)
            {
                foreach (JsonProperty property in element.EnumerateObject())
                {
                    if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
                    {
                        value = property.Value;
                        return true;
                    }
                }
            }

            value = default;
            return false;
        }

        private static string? ReadState(JsonElement deployment)
        {
            if (!TryProperty(deployment, "state", out JsonElement state))
            {
                return null;
            }

            if (state.ValueKind == JsonValueKind.String)
            {
                return state.GetString()?.Replace('-', '_').ToLowerInvariant();
            }

            if (state.ValueKind == JsonValueKind.Number && state.TryGetInt32(out int numeric))
            {
                return numeric switch
                {
                    0 => "prepared",
                    1 => "uploaded",
                    2 => "activated",
                    3 => "verified",
                    4 => "rolled_back",
                    5 => "cleaned",
                    6 => "failed",
                    _ => null,
                };
            }

            return null;
        }
    }
}
