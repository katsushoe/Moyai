using System.Text.Json;
using ModelContextProtocol.Protocol;

namespace Moyai.Cli;

/// <summary>サービスの業務結果をCLIの出力先と終了コードへ反映します。</summary>
public static class CliResponse
{
    /// <summary>成功JSONは標準出力、失敗は構造化エラーと終了コード1へ変換します。</summary>
    public static int Write(string command, CallToolResult result, TextWriter output, TextWriter error)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);
        string? text = result.Content.OfType<TextContentBlock>().FirstOrDefault()?.Text;
        string? payload = result.StructuredContent?.GetRawText() ?? text;
        if (result.IsError is true) return Fail(command, "service_error", payload, error);
        if (string.IsNullOrWhiteSpace(payload)) return Fail(command, "service_invalid_response", "Service returned no JSON result.", error);

        try
        {
            using JsonDocument document = JsonDocument.Parse(payload);
            if (IsFailure(document.RootElement)) return Fail(command, "service_operation_failed", payload, error);
            if (result.StructuredContent is not null && text is not null)
            {
                using JsonDocument fallback = JsonDocument.Parse(text);
                if (IsFailure(fallback.RootElement)) return Fail(command, "service_operation_failed", text, error);
            }
            output.WriteLine(payload);
            return 0;
        }
        catch (JsonException exception)
        {
            return Fail(command, "service_invalid_response", exception.Message, error);
        }
    }

    private static bool IsFailure(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("ok", out JsonElement ok)) return false;
        return ok.ValueKind switch
        {
            JsonValueKind.False => true,
            JsonValueKind.True => false,
            _ => throw new JsonException("Service ok field must be a boolean."),
        };
    }

    private static int Fail(string command, string code, string? detail, TextWriter error)
    {
        error.WriteLine(JsonSerializer.Serialize(new { command, summary = detail, ok = false, fatal = false, error = new { code, message = detail } }));
        return 1;
    }
}
