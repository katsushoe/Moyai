namespace Moyai.Application.Providers;

/// <summary>PR読取契約の入力です。Providerへの送信経路は契約合意後に接続します。</summary>
public sealed record PullRequestReadInput(long PullRequestId)
{
    /// <summary>Provider共通の正のPR識別子を検証します。</summary>
    public void Validate()
    {
        if (PullRequestId <= 0)
            throw new ArgumentOutOfRangeException(nameof(PullRequestId), "Pull request ID must be positive.");
    }
}
