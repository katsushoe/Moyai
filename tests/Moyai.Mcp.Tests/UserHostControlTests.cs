using System.IO.Pipes;
using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Moyai.Configuration;

namespace Moyai.Mcp.Tests;

public sealed class UserHostControlTests
{
    [Fact]
    public async Task MissingPipeReturnsNullButCallerCancellationIsPreserved()
    {
        string pipeName = "moyai-absent-" + Guid.NewGuid().ToString("N");
        Assert.Null(await UserHostControl.SendAsync("status", TimeSpan.FromMilliseconds(30), pipeName));
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            UserHostControl.SendAsync("status", TimeSpan.FromSeconds(1), pipeName, cancelled.Token));
    }

    [Fact]
    public async Task ConnectedPipeWithoutResponseIsNotReportedAsStopped()
    {
        string pipeName = "moyai-disconnect-" + Guid.NewGuid().ToString("N");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var server = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        Task disconnect = Task.Run(async () =>
        {
            await using (server)
            {
                await server.WaitForConnectionAsync(timeout.Token);
                using var reader = new StreamReader(server, leaveOpen: true);
                Assert.NotNull(await reader.ReadLineAsync(timeout.Token));
            }
        }, timeout.Token);
        await Assert.ThrowsAsync<IOException>(() => UserHostControl.SendAsync("status", TimeSpan.FromSeconds(1), pipeName, timeout.Token));
        await disconnect;
    }

    [Fact]
    public async Task StopResponseSurvivesImmediateServerExitAfterAcknowledgement()
    {
        for (int attempt = 0; attempt < 20; attempt++)
        {
            string pipeName = "moyai-stop-exit-" + Guid.NewGuid().ToString("N");
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var server = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            Task exit = Task.Run(async () =>
            {
                await using (server)
                {
                    await server.WaitForConnectionAsync(timeout.Token);
                    using var reader = new StreamReader(server, leaveOpen: true);
                    Assert.NotNull(await reader.ReadLineAsync(timeout.Token));
                    byte[] response = System.Text.Encoding.UTF8.GetBytes(JsonSerializer.Serialize(
                        new HostControlResponse(true, "stopping", Environment.ProcessId, "http://127.0.0.1:12345")) + "\n");
                    await server.WriteAsync(response, timeout.Token);
                    Assert.Equal("ack", await reader.ReadLineAsync(timeout.Token));
                }
            }, timeout.Token);
            HostControlResponse? result = await UserHostControl.SendAsync("stop", TimeSpan.FromSeconds(1), pipeName, timeout.Token);
            Assert.Equal("stopping", result?.Status);
            await exit;
        }
    }

    [Fact]
    public async Task ConnectedHostResponseHasItsOwnTimeoutBudget()
    {
        string pipeName = "moyai-budget-" + Guid.NewGuid().ToString("N");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await using var server = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        Task response = Task.Run(async () =>
        {
            await server.WaitForConnectionAsync(timeout.Token);
            using var reader = new StreamReader(server, leaveOpen: true);
            await using var writer = new StreamWriter(server, leaveOpen: true) { AutoFlush = true };
            Assert.NotNull(await reader.ReadLineAsync(timeout.Token));
            await Task.Delay(300, timeout.Token);
            await writer.WriteLineAsync(JsonSerializer.Serialize(new HostControlResponse(true, "running", Environment.ProcessId, "http://127.0.0.1:12345")));
            Assert.Equal("ack", await reader.ReadLineAsync(timeout.Token));
        }, timeout.Token);
        HostControlResponse? result = await UserHostControl.SendAsync("status", TimeSpan.FromMilliseconds(100), pipeName, timeout.Token);
        Assert.NotNull(result);
        Assert.True(result.Ok);
        await response;
    }

    [Fact]
    public async Task MalformedAndDisconnectedClientsDoNotStopHostAndStopCommandDoes()
    {
        string pipeName = "moyai-test-" + Guid.NewGuid().ToString("N");
        using var lifetime = new TestLifetime();
        using var service = new UserHostControlService(lifetime, "http://127.0.0.1:12345", NullLogger<UserHostControlService>.Instance, pipeName);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        await service.StartAsync(timeout.Token);
        try
        {
            using (var disconnected = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly))
                await disconnected.ConnectAsync(timeout.Token);
            HostControlResponse malformed = await SendAsync("invalid json");
            Assert.False(malformed.Ok);
            Assert.Equal("invalid_request", malformed.Error);
            Assert.False(lifetime.ApplicationStopping.IsCancellationRequested);
            HostControlResponse status = await SendAsync(JsonSerializer.Serialize(new HostControlRequest("status")));
            Assert.True(status.Ok);
            Assert.Equal("running", status.Status);
            var stopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            using CancellationTokenRegistration registration = lifetime.ApplicationStopping.Register(() => stopped.TrySetResult());
            HostControlResponse stop = await SendAsync(JsonSerializer.Serialize(new HostControlRequest("stop")));
            Assert.Equal("stopping", stop.Status);
            await stopped.Task.WaitAsync(timeout.Token);
            Assert.True(lifetime.ApplicationStopping.IsCancellationRequested);
        }
        finally { await service.StopAsync(timeout.Token); }

        async Task<HostControlResponse> SendAsync(string command)
        {
            await using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            await client.ConnectAsync(timeout.Token);
            using var reader = new StreamReader(client, leaveOpen: true);
            await using var writer = new StreamWriter(client, leaveOpen: true) { AutoFlush = true };
            await writer.WriteLineAsync(command.AsMemory(), timeout.Token);
            await Task.Delay(200, timeout.Token);
            string? json = await reader.ReadLineAsync(timeout.Token);
            await writer.WriteLineAsync("ack".AsMemory(), timeout.Token);
            return JsonSerializer.Deserialize<HostControlResponse>(Assert.IsType<string>(json))!;
        }
    }

    private sealed class TestLifetime : IHostApplicationLifetime, IDisposable
    {
        private readonly CancellationTokenSource _started = new();
        private readonly CancellationTokenSource _stopping = new();
        public TestLifetime() => _started.Cancel();
        public CancellationToken ApplicationStarted => _started.Token;
        public CancellationToken ApplicationStopping => _stopping.Token;
        public CancellationToken ApplicationStopped => _stopping.Token;
        public void StopApplication() => _stopping.Cancel();
        public void Dispose() { _started.Dispose(); _stopping.Dispose(); }
    }
}
