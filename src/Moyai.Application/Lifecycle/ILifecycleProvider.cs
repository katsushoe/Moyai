namespace Moyai.Application.Lifecycle;

/// <summary>Build、Release、Deploy実行境界を定義します。</summary>
public interface ILifecycleProvider
{
    string Name { get; }

    /// <summary>Providerが操作単位のAssertionで認証し、静的Service Tokenを使用しない場合にtrueを返します。</summary>
    bool UsesAssertion(LifecycleAction action) => false;

    Task<LifecycleResult> ExecuteAsync(LifecycleRequest request, CancellationToken cancellationToken = default);
}
