using System.Text.Json;

namespace Moyai.ProviderAuthentication;

/// <summary>Trust BundleとJWTのJSONおよびBase64url表現を扱います。</summary>
public static class AssertionJson
{
    /// <summary>公開Contract用のJSON設定です。</summary>
    public static JsonSerializerOptions Options { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    /// <summary>バイト列をpaddingなしBase64urlへ変換します。</summary>
    public static string Encode(ReadOnlySpan<byte> bytes) => Convert.ToBase64String(bytes)
        .TrimEnd('=')
        .Replace('+', '-')
        .Replace('/', '_');

    /// <summary>Canonical Base64urlをバイト列へ変換します。</summary>
    public static byte[] Decode(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.Length == 0 || value.Any(static character => !char.IsAsciiLetterOrDigit(character) && character is not ('-' or '_')))
        {
            throw new FormatException("Invalid base64url.");
        }

        byte[] bytes = Convert.FromBase64String(
            value.Replace('-', '+').Replace('_', '/') + new string('=', (4 - value.Length % 4) % 4));
        if (!string.Equals(Encode(bytes), value, StringComparison.Ordinal))
        {
            throw new FormatException("Noncanonical base64url.");
        }

        return bytes;
    }

    internal static JsonDocument ParseObject(byte[] data)
    {
        JsonDocument document = JsonDocument.Parse(data, new JsonDocumentOptions { MaxDepth = 8 });
        JsonElement root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object
            || root.EnumerateObject().Select(static property => property.Name).Distinct(StringComparer.Ordinal).Count()
                != root.EnumerateObject().Count())
        {
            document.Dispose();
            throw new FormatException("Invalid JSON object.");
        }

        return document;
    }
}
