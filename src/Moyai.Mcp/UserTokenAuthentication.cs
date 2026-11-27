using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using Moyai.Configuration;

namespace Moyai.Mcp;

/// <summary>Authenticates HTTP clients against the current user's profile secret.</summary>
public sealed class UserTokenAuthentication
{
    private readonly RequestDelegate _next;
    private readonly byte[] _expected;

    public UserTokenAuthentication(RequestDelegate next, string token)
    {
        MoyaiSettings.ValidateApiToken(token);
        _next = next;
        _expected = SHA256.HashData(Encoding.UTF8.GetBytes(token));
    }

    public Task InvokeAsync(HttpContext context)
    {
        var headers = context.Request.Headers.Authorization;
        if (headers.Count == 1 && AuthenticationHeaderValue.TryParse(headers[0], out var authorization)
            && authorization.Scheme.Equals("Bearer", StringComparison.OrdinalIgnoreCase)
            && authorization.Parameter is { Length: 64 } token
            && CryptographicOperations.FixedTimeEquals(_expected, SHA256.HashData(Encoding.UTF8.GetBytes(token))))
            return _next(context);
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        context.Response.Headers.WWWAuthenticate = "Bearer";
        return Task.CompletedTask;
    }
}
