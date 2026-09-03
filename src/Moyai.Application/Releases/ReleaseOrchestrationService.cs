using Moyai.Application.Lifecycle;
using Moyai.Domain.Releases;

namespace Moyai.Application.Releases;

/// <summary>Release公開Workflowと集約参照を提供します。</summary>
public sealed class ReleaseOrchestrationService(ReleaseService releases, ReleaseContentService content, LifecycleService lifecycle)
{
    /// <summary>Releaseを公開準備中へ遷移します。</summary>
    public Task<Release> PrepareAsync(string project, string version, long expectedRevision, string actorType, string actorName, CancellationToken cancellationToken = default) =>
        releases.TransitionAsync(new TransitionReleaseCommand(project, version, ReleaseStatus.Preparing, expectedRevision, actorType, actorName), cancellationToken);

    /// <summary>Releaseを公開可能状態へ遷移します。</summary>
    public Task<Release> MarkReadyAsync(string project, string version, long expectedRevision, string actorType, string actorName, CancellationToken cancellationToken = default) =>
        releases.TransitionAsync(new TransitionReleaseCommand(project, version, ReleaseStatus.Ready, expectedRevision, actorType, actorName), cancellationToken);

    /// <summary>Providerへ公開を委譲し、開始・成功・失敗状態を永続化します。</summary>
    public async Task<ReleasePublishResult> PublishAsync(string project, string version, long expectedRevision, string actorType, string actorName, CancellationToken cancellationToken = default)
    {
        Release current = await releases.GetAsync(project, version, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (current.Status == ReleaseStatus.Released) return new ReleasePublishResult(current, null, true);
        Release publishing = await releases.TransitionAsync(new TransitionReleaseCommand(project, version, ReleaseStatus.Publishing, expectedRevision, actorType, actorName), cancellationToken).ConfigureAwait(false);
        try
        {
            IReadOnlyList<ReleaseArtifact> artifacts = await content.ListArtifactsAsync(project, version, cancellationToken).ConfigureAwait(false);
            string? artifactPath = artifacts.Select(static artifact => artifact.FilePath).FirstOrDefault(static path => !string.IsNullOrWhiteSpace(path));
            LifecycleResult providerResult = await lifecycle.ExecuteAsync(project, LifecycleAction.ReleasePublish, actorType, actorName, version, artifactPath, current.ReleaseNotes, cancellationToken).ConfigureAwait(false);
            ReleaseStatus finalStatus = providerResult.Ok ? ReleaseStatus.Released : ReleaseStatus.Failed;
            Release final = await releases.TransitionAsync(new TransitionReleaseCommand(project, version, finalStatus, publishing.Revision, actorType, actorName), cancellationToken).ConfigureAwait(false);
            return new ReleasePublishResult(final, providerResult, false);
        }
        catch
        {
            await releases.TransitionAsync(new TransitionReleaseCommand(project, version, ReleaseStatus.Failed, publishing.Revision, actorType, actorName), CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>失敗したReleaseをReadyへ戻して公開を再試行します。</summary>
    public async Task<ReleasePublishResult> RetryAsync(string project, string version, long expectedRevision, string actorType, string actorName, CancellationToken cancellationToken = default)
    {
        Release ready = await releases.TransitionAsync(new TransitionReleaseCommand(project, version, ReleaseStatus.Ready, expectedRevision, actorType, actorName), cancellationToken).ConfigureAwait(false);
        return await PublishAsync(project, version, ready.Revision, actorType, actorName, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Providerで公開停止後にReleaseをWithdrawnへ遷移します。</summary>
    public async Task<ReleasePublishResult> WithdrawAsync(string project, string version, long expectedRevision, string actorType, string actorName, CancellationToken cancellationToken = default)
    {
        Release current = await releases.GetAsync(project, version, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (current.Status == ReleaseStatus.Withdrawn) return new ReleasePublishResult(current, null, true);
        if (current.Revision != expectedRevision) throw new InvalidOperationException($"Release revision conflict. Expected {expectedRevision}, actual {current.Revision}.");
        LifecycleResult providerResult = await lifecycle.ExecuteAsync(project, LifecycleAction.ReleaseWithdraw, actorType, actorName, version, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!providerResult.Ok) return new ReleasePublishResult(current, providerResult, false);
        Release withdrawn = await releases.TransitionAsync(new TransitionReleaseCommand(project, version, ReleaseStatus.Withdrawn, expectedRevision, actorType, actorName), cancellationToken).ConfigureAwait(false);
        return new ReleasePublishResult(withdrawn, providerResult, false);
    }

    /// <summary>最新の公開済みStable Releaseを返します。</summary>
    public async Task<Release?> LatestAsync(string project, CancellationToken cancellationToken = default) =>
        (await releases.ListAsync(project, cancellationToken: cancellationToken).ConfigureAwait(false))
            .Where(static release => release.Status == ReleaseStatus.Released && release.Channel == ReleaseChannel.Stable)
            .OrderByDescending(static release => release.ReleasedAt)
            .ThenByDescending(static release => release.Id)
            .FirstOrDefault();

    /// <summary>ReleaseとWorkItem関連、Artifactをまとめて返します。</summary>
    public async Task<ReleaseOverview> OverviewAsync(string project, string version, CancellationToken cancellationToken = default) =>
        new(await releases.GetAsync(project, version, cancellationToken: cancellationToken).ConfigureAwait(false), await content.ListItemsAsync(project, version, cancellationToken).ConfigureAwait(false), await content.ListArtifactsAsync(project, version, cancellationToken).ConfigureAwait(false));
}

/// <summary>Release公開操作の結果です。</summary>
public sealed record ReleasePublishResult(Release Release, LifecycleResult? ProviderResult, bool AlreadyCompleted);

/// <summary>Releaseの集約表示です。</summary>
public sealed record ReleaseOverview(Release Release, IReadOnlyList<ReleaseWorkItem> Items, IReadOnlyList<ReleaseArtifact> Artifacts);
