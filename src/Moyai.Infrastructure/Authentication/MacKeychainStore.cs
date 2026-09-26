using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using Moyai.Application.Authentication;

namespace Moyai.Infrastructure.Authentication;

/// <summary>macOSのユーザーKeychainへ専用Service/AccountでKEKを格納します。</summary>
[SupportedOSPlatform("macos")]
public sealed class MacKeychainStore(string keyNamespace) : IProtectedKeyStore
{
    public Task StoreAsync(string version, ReadOnlyMemory<byte> key, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (SecKeychainSetUserInteractionAllowed(0) != 0) throw new ProviderAuthenticationException("auth_key_provider_unavailable");
        byte[] service = Encoding.UTF8.GetBytes(keyNamespace);
        byte[] account = Encoding.UTF8.GetBytes(version);
        byte[] data = key.ToArray();
        try
        {
            int status = SecKeychainAddGenericPassword(0, (uint)service.Length, service, (uint)account.Length, account, (uint)data.Length, data, 0);
            if (status != 0) throw new ProviderAuthenticationException("auth_key_provider_unavailable");
            return Task.CompletedTask;
        }
        finally { System.Security.Cryptography.CryptographicOperations.ZeroMemory(data); }
    }

    public Task<byte[]> LoadAsync(string version, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (SecKeychainSetUserInteractionAllowed(0) != 0) throw new ProviderAuthenticationException("auth_key_provider_unavailable");
        if (string.IsNullOrWhiteSpace(version)) throw new ProviderAuthenticationException("auth_key_provider_unavailable");
        byte[] service = Encoding.UTF8.GetBytes(keyNamespace);
        byte[] account = Encoding.UTF8.GetBytes(version);
        int status = SecKeychainFindGenericPassword(0, (uint)service.Length, service, (uint)account.Length, account, out uint length, out nint data, 0);
        if (status != 0) throw new ProviderAuthenticationException("auth_key_provider_unavailable");
        try
        {
            if (length != 32) throw new ProviderAuthenticationException("auth_key_provider_unavailable");
            byte[] key = new byte[32];
            Marshal.Copy(data, key, 0, key.Length);
            return Task.FromResult(key);
        }
        finally { _ = SecKeychainItemFreeContent(0, data); }
    }

    [DllImport("/System/Library/Frameworks/Security.framework/Security", ExactSpelling = true)]
    private static extern int SecKeychainSetUserInteractionAllowed(byte state);
    [DllImport("/System/Library/Frameworks/Security.framework/Security", ExactSpelling = true)]
    private static extern int SecKeychainAddGenericPassword(nint keychain, uint serviceLength, byte[] service, uint accountLength, byte[] account, uint passwordLength, byte[] password, nint item);
    [DllImport("/System/Library/Frameworks/Security.framework/Security", ExactSpelling = true)]
    private static extern int SecKeychainFindGenericPassword(nint keychain, uint serviceLength, byte[] service, uint accountLength, byte[] account, out uint passwordLength, out nint password, nint item);
    [DllImport("/System/Library/Frameworks/Security.framework/Security", ExactSpelling = true)]
    private static extern int SecKeychainItemFreeContent(nint attributes, nint data);
}
