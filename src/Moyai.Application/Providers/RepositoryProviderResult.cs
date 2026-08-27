namespace Moyai.Application.Providers;

/// <summary>Repository Providerの標準化された応答を表します。</summary>
public sealed record RepositoryProviderResult(bool Ok, string Operation, string? Output, string? ErrorCode, string? ErrorMessage);
