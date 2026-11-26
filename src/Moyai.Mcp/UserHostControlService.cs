using System.IO.Pipes;
using System.Text.Json;
using Moyai.Configuration;

namespace Moyai.Mcp;

public sealed partial class UserHostControlService(IHostApplicationLifetime lifetime, string serverUrl, ILogger<UserHostControlService> logger, string? pipeName = null, LoadedConfigurationSnapshot? configuration = null) : BackgroundService
{
    private readonly DateTimeOffset _hostStartedAt = ReadProcessStart();

    private static DateTimeOffset ReadProcessStart()
    {
        using var process = System.Diagnostics.Process.GetCurrentProcess();
        return new(process.StartTime.ToUniversalTime());
    }
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using CancellationTokenRegistration registration = lifetime.ApplicationStarted.Register(() => started.TrySetResult());
        await started.Task.WaitAsync(stoppingToken);
        while (!stoppingToken.IsCancellationRequested)
        {
            await using var pipe = new NamedPipeServerStream(pipeName ?? UserHostControl.PipeName, PipeDirection.InOut, 1,
                PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            await pipe.WaitForConnectionAsync(stoppingToken);
            using var requestTimeout = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            requestTimeout.CancelAfter(TimeSpan.FromSeconds(10));
            try { await HandleAsync(pipe, requestTimeout.Token); }
            catch (IOException exception) { LogDisconnectedClient(logger, exception); }
            catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested) { }
        }
    }

    private async Task HandleAsync(NamedPipeServerStream pipe, CancellationToken cancellationToken)
    {
            using var reader = new StreamReader(pipe, leaveOpen: true);
            await using var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };
            string? line = await reader.ReadLineAsync(cancellationToken);
            HostControlRequest? request;
            try { request = line is null ? null : JsonSerializer.Deserialize<HostControlRequest>(line); }
            catch (JsonException exception)
            {
                LogMalformedRequest(logger, exception);
                await writer.WriteLineAsync(JsonSerializer.Serialize(new HostControlResponse(false, "error", Environment.ProcessId, serverUrl, "invalid_request")));
                _ = await reader.ReadLineAsync(cancellationToken);
                return;
            }
            HostControlResponse response = request?.Command switch
            {
                "status" => new(true, "running", Environment.ProcessId, serverUrl, Configuration: configuration?.Read(), HostStartedAtUtc: _hostStartedAt),
                "stop" => new(true, "stopping", Environment.ProcessId, serverUrl),
                _ => new(false, "error", Environment.ProcessId, serverUrl, "unsupported_command"),
            };
            try
            {
                await writer.WriteLineAsync(JsonSerializer.Serialize(response));
                _ = await reader.ReadLineAsync(cancellationToken);
            }
            finally
            {
                if (request?.Command == "stop") lifetime.StopApplication();
            }
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "[HostControl] Control client disconnected.")]
    private static partial void LogDisconnectedClient(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "[HostControl] Rejected malformed control request.")]
    private static partial void LogMalformedRequest(ILogger logger, Exception exception);
}
