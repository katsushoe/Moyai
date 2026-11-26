using System.Text.Json;

namespace Moyai.Application.Providers;

/// <summary>省略と明示値を区別する設定部分更新要求です。</summary>
public sealed class RepositorySettingsChanges
{
    private RepositorySettingsChanges(IReadOnlyDictionary<string, JsonElement> values) => Values = values;

    /// <summary>検証済みの指定項目だけを保持します。</summary>
    public IReadOnlyDictionary<string, JsonElement> Values { get; }

    /// <summary>設定項目、型、作者の組を検証して独立した要求を作成します。</summary>
    public static RepositorySettingsChanges Create(IReadOnlyDictionary<string, JsonElement> changes)
    {
        ArgumentNullException.ThrowIfNull(changes);
        if (changes.Count == 0) throw new ArgumentException("Settings changes must not be empty.", nameof(changes));
        var values = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach ((string name, JsonElement value) in changes)
        {
            bool valid = name switch
            {
                "direct_push_branches" or "pull_branches" or "protected_branches" or "local_commit_branches" => value.ValueKind == JsonValueKind.Array && value.EnumerateArray().All(static item => item.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(item.GetString())),
                "pr_publication_source_branches" => ValidPublicationSources(value),
                "pull_request_routes" => ValidPullRequestRoutes(value),
                "require_clean_working_tree" => value.ValueKind is JsonValueKind.True or JsonValueKind.False,
                "tag_target_branch" or "tag_pattern" or "commit_author_name" or "commit_author_email" => value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString()),
                _ => false,
            };
            if (!valid) throw new ArgumentException("Settings field or value is invalid.", nameof(changes));
            values.Add(name, value.Clone());
        }
        if (values.ContainsKey("commit_author_name") != values.ContainsKey("commit_author_email"))
            throw new ArgumentException("Commit author name and email must be specified together.", nameof(changes));
        if (values.TryGetValue("commit_author_email", out JsonElement email)
            && (!System.Net.Mail.MailAddress.TryCreate(email.GetString(), out var address) || address.Address != email.GetString()))
            throw new ArgumentException("Commit author email is invalid.", nameof(changes));
        return new RepositorySettingsChanges(new System.Collections.ObjectModel.ReadOnlyDictionary<string, JsonElement>(values));
    }

    internal static bool ValidPublicationSources(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Array) return false;
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (JsonElement item in value.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String || item.GetString() is not { Length: > 0 and <= 255 } branch
                || branch == "@" || branch.StartsWith('-') || branch.StartsWith("refs/", StringComparison.Ordinal)
                || branch.Contains("..", StringComparison.Ordinal) || branch.Contains("@{", StringComparison.Ordinal)
                || branch.Split('/').Any(static part => part.Length == 0 || part.StartsWith('.') || part.EndsWith('.')
                    || part.EndsWith(".lock", StringComparison.OrdinalIgnoreCase))
                || branch.Any(static character => char.IsWhiteSpace(character) || char.IsControl(character) || "~^:?*[\\".Contains(character, StringComparison.Ordinal))
                || !names.Add(branch)) return false;
        }
        return true;
    }

    internal static bool ValidPullRequestRoutes(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Array) return false;
        var routes = new HashSet<(string Source, string Destination)>();
        foreach (JsonElement item in value.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object || item.EnumerateObject().Count() != 2
                || !item.TryGetProperty("source_branch", out JsonElement source) || !item.TryGetProperty("destination_branch", out JsonElement destination)
                || !RouteBranch(source, out string from) || !RouteBranch(destination, out string to)
                || !routes.Add((from, to))) return false;
        }
        return true;
    }

    private static bool RouteBranch(JsonElement value, out string branch)
    {
        branch = value.ValueKind == JsonValueKind.String ? value.GetString()! : "";
        return !string.IsNullOrWhiteSpace(branch) && !branch.StartsWith('-') && branch.IndexOfAny([' ', '\t', '\r', '\n']) < 0;
    }
}
