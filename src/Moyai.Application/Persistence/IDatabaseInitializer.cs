namespace Moyai.Application.Persistence;

/// <summary>Moyaiデータベースを初期化します。</summary>
public interface IDatabaseInitializer
{
    /// <summary>必要な設定とスキーマを非同期で適用します。</summary>
    Task InitializeAsync(CancellationToken cancellationToken = default);
}
