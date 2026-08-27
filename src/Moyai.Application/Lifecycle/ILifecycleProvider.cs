namespace Moyai.Application.Lifecycle;

/// <summary>Build、Release、Deploy実行境界を定義します。</summary>
public interface ILifecycleProvider
{
    string Name { get; }
    Task<LifecycleResult> ExecuteAsync(LifecycleRequest request, CancellationToken cancellationToken = default);
}
