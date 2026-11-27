namespace Moyai.Mcp;

/// <summary>トレイ用に、機密情報を含まないプロセスの稼働状態と版だけを公開します。</summary>
public sealed class HealthEndpoint(RequestDelegate next)
{
    /// <summary>GET /health だけを認証前に処理し、その他は既存の認証へ渡します。</summary>
    public Task InvokeAsync(HttpContext context) => HttpMethods.IsGet(context.Request.Method)
        && context.Request.Path == "/health"
        ? context.Response.WriteAsJsonAsync(new { status = "ok", version = typeof(HealthEndpoint).Assembly.GetName().Version!.ToString() }, context.RequestAborted)
        : next(context);
}
