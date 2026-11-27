namespace Moyai.Application.Providers;

/// <summary>Provider既定経路へ依存しないPR作成入力です。</summary>
public sealed record PullRequestInput(string SourceBranch, string DestinationBranch, string Title, string Description, bool Draft)
{
    /// <summary>入力形式だけを検証し、許可経路の判断はProviderへ委ねます。</summary>
    public void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(Title);
        ArgumentNullException.ThrowIfNull(Description);
        if (Title.Length > 255 || Title.Any(char.IsControl) || Description.Length > 32768 || Description.Contains('\0', StringComparison.Ordinal))
            throw new ArgumentException("Pull request title or description is invalid.");
        ValidateBranch(SourceBranch);
        ValidateBranch(DestinationBranch);
    }

    private static void ValidateBranch(string? value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        if (value is "HEAD" or "@" || value.StartsWith('-') || value.StartsWith('/') || value.EndsWith('/') || value.EndsWith('.')
            || value.StartsWith("refs/", StringComparison.Ordinal) || value.Contains("..", StringComparison.Ordinal) || value.Contains("@{", StringComparison.Ordinal)
            || value.Any(static c => char.IsWhiteSpace(c) || char.IsControl(c) || "~^:?*[\\".Contains(c, StringComparison.Ordinal))
            || value.Split('/').Any(static part => part.Length == 0 || part.StartsWith('.') || part.EndsWith(".lock", StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException("Pull request branches must be explicit literal names.");
    }
}
