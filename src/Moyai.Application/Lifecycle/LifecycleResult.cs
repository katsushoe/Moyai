namespace Moyai.Application.Lifecycle;

/// <summary>Lifecycle Providerの標準応答を表します。</summary>
public sealed record LifecycleResult(
    bool Ok,
    string Operation,
    string? Output,
    string? ErrorCode,
    string? ErrorMessage,
    long? ResourceId = null,
    bool AlreadyCompleted = false);
