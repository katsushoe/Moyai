namespace Moyai.Application.Lifecycle;

/// <summary>Lifecycle操作結果の監査Event保存境界を定義します。</summary>
public interface ILifecycleEventWriter
{
    Task WriteAsync(Guid projectId, LifecycleAction action, LifecycleResult result, string actorType, string actorName, CancellationToken cancellationToken = default);
}
