namespace Moyai.Domain.Releases;

/// <summary>Releaseの状態です。</summary>
public enum ReleaseStatus
{
    Draft,
    Planned,
    Preparing,
    Ready,
    Publishing,
    Released,
    Failed,
    Withdrawn,
}
