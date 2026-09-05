using System.Text.Json;
using ModelContextProtocol.Protocol;
using Moyai.Application.Lifecycle;

namespace Moyai.Infrastructure.Providers;

/// <summary>Lifecycle Providerの通信結果と業務結果を標準応答へ変換します。</summary>
public static class LifecycleProviderResponse
{
    public static LifecycleResult Parse(string operation, CallToolResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        string? text = result.Content.OfType<TextContentBlock>().FirstOrDefault()?.Text;
        string? output = result.StructuredContent?.GetRawText() ?? text;
        if (result.IsError is true) return Failure(operation, output);
        if (string.IsNullOrWhiteSpace(output)) return Invalid(operation, "Provider returned no JSON result.");
        try
        {
            using JsonDocument document = JsonDocument.Parse(output);
            if (!document.RootElement.TryGetProperty("ok", out JsonElement ok) || ok.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                return Invalid(operation, "Provider result must contain a boolean ok field.");
            return ok.GetBoolean()
                ? new LifecycleResult(true, operation, output, null, null, FindResourceId(document.RootElement))
                : Failure(operation, output);
        }
        catch (JsonException exception)
        {
            return Invalid(operation, exception.Message);
        }
    }

    private static LifecycleResult Failure(string operation, string? detail) =>
        new(false, operation, null, RepositoryProviderContract.NormalizeErrorCode(detail), detail);

    private static LifecycleResult Invalid(string operation, string detail) =>
        new(false, operation, null, "provider_invalid_response", detail);

    private static long? FindResourceId(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            if (element.TryGetProperty("id", out JsonElement id) && id.TryGetInt64(out long value)) return value;
            foreach (JsonProperty property in element.EnumerateObject())
            {
                long? nested = FindResourceId(property.Value);
                if (nested is not null) return nested;
            }
        }
        return null;
    }
}
