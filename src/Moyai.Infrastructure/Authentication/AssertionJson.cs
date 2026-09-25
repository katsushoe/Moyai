using System.Text.Json;

namespace Moyai.Infrastructure.Authentication;

/// <summary>JWTとEnvelopeのCanonical JSON、Base64url表現です。</summary>
public static class AssertionJson
{
    public static JsonSerializerOptions Options { get; } = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };
    public static string Encode(ReadOnlySpan<byte> bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    public static byte[] Decode(string value)
    {
        if (value.Length == 0 || value.Any(static c => !char.IsAsciiLetterOrDigit(c) && c is not ('-' or '_')))
            throw new FormatException("Invalid base64url.");
        byte[] bytes = Convert.FromBase64String(value.Replace('-', '+').Replace('_', '/') + new string('=', (4 - value.Length % 4) % 4));
        if (Encode(bytes) != value) throw new FormatException("Noncanonical base64url.");
        return bytes;
    }

    public static JsonDocument Parse(byte[] data)
    {
        JsonDocument document = JsonDocument.Parse(data, new JsonDocumentOptions { MaxDepth = 8 });
        if (document.RootElement.ValueKind != JsonValueKind.Object
            || document.RootElement.EnumerateObject().Select(static x => x.Name).Distinct(StringComparer.Ordinal).Count()
                != document.RootElement.EnumerateObject().Count())
        {
            document.Dispose();
            throw new FormatException("Invalid JSON object.");
        }
        return document;
    }
}
