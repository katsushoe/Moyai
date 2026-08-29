namespace Moyai.Domain.Releases;

/// <summary>許可されていないRelease状態遷移を表します。</summary>
public sealed class InvalidReleaseTransitionException : InvalidOperationException
{
    public InvalidReleaseTransitionException(ReleaseStatus current, ReleaseStatus next)
        : base($"Release cannot transition from '{current}' to '{next}'.")
    {
    }
}
