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
}
