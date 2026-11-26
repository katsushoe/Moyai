namespace Moyai.Application.Providers;

/// <summary>期待するsource commitと明示方式を含むPRマージ契約の入力です。</summary>
public sealed record PullRequestMergeInput(long PullRequestId, string Strategy, string ExpectedSourceCommit, string? Message = null)
{
    /// <summary>共通入力形式を検証します。Providerの対応方式や保護条件は判断しません。</summary>
    public void Validate()
    {
        new PullRequestReadInput(PullRequestId).Validate();
        if (Strategy is not ("repository_default" or "merge" or "squash" or "rebase" or "fast_forward"))
            throw new ArgumentException("An explicit supported common merge strategy is required.", nameof(Strategy));
        if (ExpectedSourceCommit is null || ExpectedSourceCommit.Length != 40 || !ExpectedSourceCommit.All(Uri.IsHexDigit))
            throw new ArgumentException("Expected source commit must be a full 40-character hexadecimal SHA.", nameof(ExpectedSourceCommit));
        if (Message is not null && string.IsNullOrWhiteSpace(Message))
            throw new ArgumentException("Merge message must be omitted or nonblank.", nameof(Message));
    }
}
