using System.IO.Pipes;
using System.Security.Principal;
using System.Text.Json;

namespace Moyai.Configuration;

public sealed record HostControlRequest(string Command);

public sealed record HostControlResponse(bool Ok, string Status, int ProcessId, string ServerUrl, string? Error = null,
    HostConfigurationState? Configuration = null, DateTimeOffset? HostStartedAtUtc = null);

public static class UserHostControl
{
    public static string PipeName
    {
        get
        {
            string identity = OperatingSystem.IsWindows()
                ? WindowsIdentity.GetCurrent().User?.Value ?? throw new InvalidOperationException("Current Windows SID is unavailable.")
                : Environment.UserName;
            return "moyai-host-" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(identity)))[..24];
        }
    }

    public static Task<HostControlResponse?> SendAsync(string command, TimeSpan timeout, CancellationToken cancellationToken = default)
        => SendAsync(command, timeout, null, cancellationToken);

    public static async Task<HostControlResponse?> SendAsync(string command, TimeSpan timeout, string? pipeName, CancellationToken cancellationToken = default)
    {
        string request = JsonSerializer.Serialize(new HostControlRequest(command));
        using var connection = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        await using var pipe = new NamedPipeClientStream(".", pipeName ?? PipeName, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        connection.CancelAfter(timeout);
        try
        {
            await pipe.ConnectAsync(connection.Token);
        }
        catch (TimeoutException)
        {
            return null;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
        using var responseTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        responseTimeout.CancelAfter(TimeSpan.FromSeconds(10));
        using var reader = new StreamReader(pipe, leaveOpen: true);
        try
        {
            // ACK後にStreamWriterを破棄すると、終了済みサーバーへの再flushが発生し得ます。
            await pipe.WriteAsync(System.Text.Encoding.UTF8.GetBytes(request + "\n"), responseTimeout.Token);
            string? response = await reader.ReadLineAsync(responseTimeout.Token);
            HostControlResponse result = response is null ? throw new IOException("Moyai host disconnected before returning its control response.")
                : JsonSerializer.Deserialize<HostControlResponse>(response) ?? throw new IOException("Moyai host returned an empty control response.");
            await pipe.WriteAsync("ack\n"u8.ToArray(), responseTimeout.Token);
            return result;
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException("Moyai host connected but did not complete its control response within 10 seconds. Check host status before repeating a lifecycle command.", exception);
        }
    }
}
