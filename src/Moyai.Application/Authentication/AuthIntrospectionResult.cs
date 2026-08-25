namespace Moyai.Application.Authentication;

/// <summary>内部Service Token検証結果を表します。</summary>
public sealed record AuthIntrospectionResult(bool Valid, string? ErrorCode)
{
    public static AuthIntrospectionResult Success { get; } = new(true, null);
}
