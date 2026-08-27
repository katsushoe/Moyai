namespace Moyai.Domain.WorkItems;

/// <summary>組み込み作業項目ワークフローを提供します。</summary>
public static class WorkItemWorkflow
{
    private static readonly Dictionary<WorkItemType, string> InitialStatuses =
        new Dictionary<WorkItemType, string>
        {
            [WorkItemType.Issue] = "open",
            [WorkItemType.Bug] = "reported",
            [WorkItemType.ChangeRequest] = "proposed",
            [WorkItemType.Feature] = "proposed",
            [WorkItemType.Risk] = "identified",
            [WorkItemType.Decision] = "proposed",
        };

    private static readonly Dictionary<WorkItemType, HashSet<(string From, string To)>> Transitions =
        new Dictionary<WorkItemType, HashSet<(string From, string To)>>
        {
            [WorkItemType.Issue] = Set(("open", "triaged"), ("triaged", "in_progress"), ("in_progress", "resolved"), ("resolved", "closed"), ("resolved", "open"), ("closed", "open")),
            [WorkItemType.Bug] = Set(("reported", "confirmed"), ("confirmed", "in_progress"), ("in_progress", "fixed"), ("fixed", "verified"), ("verified", "closed"), ("fixed", "confirmed"), ("verified", "confirmed"), ("closed", "confirmed")),
            [WorkItemType.ChangeRequest] = Set(("proposed", "reviewing"), ("reviewing", "approved"), ("approved", "implementing"), ("implementing", "implemented"), ("implemented", "verified"), ("verified", "closed"), ("implemented", "implementing"), ("verified", "implementing"), ("closed", "reviewing")),
            [WorkItemType.Feature] = Set(("proposed", "planned"), ("planned", "in_progress"), ("in_progress", "implemented"), ("implemented", "verified"), ("verified", "closed")),
            [WorkItemType.Risk] = Set(("identified", "assessed"), ("assessed", "mitigating"), ("mitigating", "monitored"), ("monitored", "closed")),
            [WorkItemType.Decision] = Set(("proposed", "reviewing"), ("reviewing", "decided"), ("decided", "superseded")),
        };

    /// <summary>指定種別の初期状態を返します。</summary>
    public static string GetInitialStatus(WorkItemType type) => InitialStatuses[type];

    /// <summary>指定した状態遷移が許可されているかを返します。</summary>
    public static bool CanTransition(WorkItemType type, string from, string to)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(from);
        ArgumentException.ThrowIfNullOrWhiteSpace(to);
        return Transitions[type].Contains((from, to));
    }

    private static HashSet<(string From, string To)> Set(params (string From, string To)[] transitions) =>
        new HashSet<(string From, string To)>(transitions);
}
