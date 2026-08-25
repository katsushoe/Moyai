namespace Moyai.Domain.WorkItems;

/// <summary>許可されていない作業項目状態遷移を表します。</summary>
public sealed class InvalidWorkItemTransitionException : InvalidOperationException
{
    /// <summary>例外を生成します。</summary>
    public InvalidWorkItemTransitionException(WorkItemType type, string from, string to)
        : base($"Transition from '{from}' to '{to}' is not allowed for '{type}'.")
    {
    }
}
