namespace Moyai.Application.Lifecycle;

/// <summary>開発ライフサイクル操作を表します。</summary>
public enum LifecycleAction
{
    Build,
    BuildClean,
    ReleaseCreate,
    ReleasePublish,
    ReleaseWithdraw,
    Deploy,
}
