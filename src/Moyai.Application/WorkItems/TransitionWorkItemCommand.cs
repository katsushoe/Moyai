namespace Moyai.Application.WorkItems;

/// <summary>WorkItemの状態遷移入力を表します。</summary>
public sealed record TransitionWorkItemCommand(
    string Project,
    string Key,
    string NextStatus,
    long ExpectedRevision,
    string ActorType,
    string ActorName);
