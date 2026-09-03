using System.Globalization;
using System.Text.Json;

namespace Moyai.Cli;

/// <summary>Converts CLI options using the service's advertised input schema.</summary>
public static class CliArguments
{
    public static Dictionary<string, string?> Parse(string[] arguments)
    {
        var values = new Dictionary<string, string?>(StringComparer.Ordinal);
        for (int index = 0; index < arguments.Length; index++)
        {
            string option = arguments[index];
            if (!option.StartsWith("--", StringComparison.Ordinal) || option.Length == 2) throw new ArgumentException("Expected --option.");
            string? value = index + 1 < arguments.Length && !arguments[index + 1].StartsWith("--", StringComparison.Ordinal) ? arguments[++index] : null;
            if (!values.TryAdd(option[2..], value)) throw new ArgumentException($"Duplicate option {option}.");
        }
        return values;
    }

    public static IReadOnlyDictionary<string, object?> Convert(Dictionary<string, string?> values, JsonElement schema, string toolName)
    {
        var result = new Dictionary<string, object?>(StringComparer.Ordinal);
        JsonElement properties = schema.GetProperty("properties");
        foreach ((string key, string? value) in values)
        {
            string name = string.Concat(key.Split('-').Select((part, index) => index == 0 ? part : char.ToUpperInvariant(part[0]) + part[1..]));
            if (toolName == "comment_add") name = name switch { "actorType" => "authorType", "actorName" => "authorName", _ => name };
            if (!properties.TryGetProperty(name, out JsonElement property)) throw new ArgumentException($"Unknown option --{key}.");
            string? type = property.TryGetProperty("type", out JsonElement element)
                ? element.ValueKind == JsonValueKind.Array ? element.EnumerateArray().First(item => item.GetString() != "null").GetString() : element.GetString()
                : "string";
            object? converted = type switch
            {
                "boolean" => value is null || bool.Parse(value),
                "integer" => long.Parse(value ?? throw new ArgumentException($"--{key} requires a value."), CultureInfo.InvariantCulture),
                "number" => double.Parse(value ?? throw new ArgumentException($"--{key} requires a value."), CultureInfo.InvariantCulture),
                "array" => (value ?? throw new ArgumentException($"--{key} requires a value.")).Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries),
                _ => value ?? throw new ArgumentException($"--{key} requires a value."),
            };
            result.Add(name, converted);
        }
        if (schema.TryGetProperty("required", out JsonElement required))
            foreach (JsonElement name in required.EnumerateArray())
            {
                string key = name.GetString()!;
                if (result.ContainsKey(key)) continue;
                JsonElement property = properties.GetProperty(key);
                if (property.TryGetProperty("type", out JsonElement type) && type.ValueKind == JsonValueKind.Array &&
                    type.EnumerateArray().Any(value => value.GetString() == "null")) result.Add(key, null);
                else throw new ArgumentException($"Missing required option: {key}.");
            }
        return result;
    }
}
