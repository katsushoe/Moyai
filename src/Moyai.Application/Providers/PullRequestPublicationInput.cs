namespace Moyai.Application.Providers;

/// <summary>固定commitの限定公開に必要な明示入力です。</summary>
public sealed record PullRequestPublicationInput(string SourceBranch, string DestinationBranch, string ExpectedSourceSha,
    string Title, string OperationId, bool Draft, string Description = "")
{
    public void Validate()
    {
        ValidateOperationId(OperationId);
        BranchProtectionEdit.ValidateBranch(SourceBranch);
        BranchProtectionEdit.ValidateBranch(DestinationBranch);
        foreach (string branch in new[] { SourceBranch, DestinationBranch })
            if (branch == "HEAD" || branch.StartsWith('-') || branch.StartsWith("refs/", StringComparison.Ordinal) || branch.Any(char.IsWhiteSpace))
                throw new ArgumentException("An explicit local branch is required.");
        if (SourceBranch == DestinationBranch || !IsSha(ExpectedSourceSha) || string.IsNullOrWhiteSpace(Title)
            || Title.Length > 256 || Title.Any(char.IsControl) || Description is null || Description.Length > 65536)
            throw new ArgumentException("Invalid PR publication input.");
    }

    public static void ValidateOperationId(string value)
    {
        if (!Guid.TryParseExact(value, "D", out _)) throw new ArgumentException("An explicit D-format operation UUID is required.");
    }

    internal static bool IsSha(string? value) => value is { Length: 40 } && value.All(Uri.IsHexDigit);
}
