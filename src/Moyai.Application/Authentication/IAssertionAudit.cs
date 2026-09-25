namespace Moyai.Application.Authentication;

/// <summary>秘密を受け取らない認証監査Contractです。</summary>
public interface IAssertionAudit
{
    Task WriteAsync(AssertionContext context, string keyId, string resultCode, CancellationToken cancellationToken = default);
}
