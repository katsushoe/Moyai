namespace Moyai.Application.Providers;

/// <summary>Provider固有Repository操作の境界を定義します。</summary>
public interface IRepositoryProvider
{
    string Name { get; }
    Task<RepositoryProviderResult> ExecuteAsync(RepositoryProviderRequest request, CancellationToken cancellationToken = default);
}
