using System.Diagnostics;
using System.Runtime.Versioning;
using Moyai.Application.Authentication;

namespace Moyai.Infrastructure.Authentication;

/// <summary>Linux Secret Serviceのsecret-toolを使用します。秘密は標準入力/出力だけで扱います。</summary>
[SupportedOSPlatform("linux")]
public sealed class SecretServiceKeyStore(string executablePath, string keyNamespace) : IProtectedKeyStore
{
    public async Task StoreAsync(string version, ReadOnlyMemory<byte> key, CancellationToken cancellationToken = default)
    {
        _ = await RunAsync(["store", "--label=" + keyNamespace, "service", keyNamespace, "version", version], Convert.ToBase64String(key.Span), cancellationToken).ConfigureAwait(false);
    }
    public async Task<byte[]> LoadAsync(string version, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(version)) throw Unavailable();
        string value = await RunAsync(["lookup", "service", keyNamespace, "version", version], null, cancellationToken).ConfigureAwait(false);
        try { return Convert.FromBase64String(value.Trim()); }
        catch (FormatException) { throw Unavailable(); }
    }
    private async Task<string> RunAsync(string[] arguments, string? input, CancellationToken cancellationToken)
    {
        if (!Path.IsPathFullyQualified(executablePath)) throw Unavailable();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        var start = new ProcessStartInfo(executablePath) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (string argument in arguments) start.ArgumentList.Add(argument);
        using var process = new Process { StartInfo = start };
        try
        {
            if (!process.Start()) throw Unavailable();
            Task<string> output = process.StandardOutput.ReadToEndAsync(timeout.Token);
            Task<string> error = process.StandardError.ReadToEndAsync(timeout.Token);
            if (input is not null) await process.StandardInput.WriteLineAsync(input.AsMemory(), timeout.Token).ConfigureAwait(false);
            process.StandardInput.Close();
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            _ = await error.ConfigureAwait(false);
            string result = await output.ConfigureAwait(false);
            if (process.ExitCode != 0 || result.Length > 1024) throw Unavailable();
            return result;
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or IOException or InvalidOperationException)
        { throw Unavailable(); }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        { throw Unavailable(); }
        finally
        {
            try { if (process.Id != 0 && !process.HasExited) process.Kill(true); }
            catch (InvalidOperationException) { /* Process did not start; no resource remains. */ }
        }
    }
    private static ProviderAuthenticationException Unavailable() => new("auth_key_provider_unavailable");
}
