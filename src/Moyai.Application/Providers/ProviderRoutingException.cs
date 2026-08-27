namespace Moyai.Application.Providers;

/// <summary>Provider Routingを安全に実行できない場合のエラーです。</summary>
public sealed class ProviderRoutingException(string code, string message) : InvalidOperationException(message)
{
    public string Code { get; } = code;
}
