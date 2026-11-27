using System.Globalization;

namespace Moyai.Mcp;

/// <summary>Owns the user host log independently of the launching CLI process.</summary>
public sealed class UserFileLoggerProvider : ILoggerProvider
{
    private readonly StreamWriter _writer;
    private readonly object _gate = new();

    public UserFileLoggerProvider(string directory)
    {
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, $"host-{DateTimeOffset.Now:yyyyMMdd}-{Environment.ProcessId}.log");
        _writer = new StreamWriter(new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read)) { AutoFlush = true };
    }

    public ILogger CreateLogger(string categoryName) => new UserFileLogger(this, categoryName);

    public void Dispose()
    {
        lock (_gate) _writer.Dispose();
    }

    private sealed class UserFileLogger(UserFileLoggerProvider provider, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information && logLevel != LogLevel.None;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;
            lock (provider._gate)
            {
                provider._writer.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{DateTimeOffset.Now:O} [{logLevel}] {category}: {formatter(state, exception)}"));
                if (exception is not null) provider._writer.WriteLine(exception);
            }
        }
    }
}
