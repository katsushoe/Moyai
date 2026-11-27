using Microsoft.Extensions.Logging;

namespace Moyai.Mcp.Tests;

public sealed class UserFileLoggerTests
{
    [Fact]
    public void HostLogOwnsWriterAndSerializesConcurrentWrites()
    {
        string directory = Path.Combine(Path.GetTempPath(), "moyai-log-" + Guid.NewGuid().ToString("N"));
        try
        {
            using (var provider = new UserFileLoggerProvider(directory))
            {
                ILogger logger = provider.CreateLogger("user-host");
                Parallel.For(0, 100, index => logger.Log(LogLevel.Information, new EventId(index), index, null,
                    static (value, _) => value.ToString(System.Globalization.CultureInfo.InvariantCulture)));
            }
            string path = Assert.Single(Directory.GetFiles(directory));
            string[] lines = File.ReadAllLines(path);
            Assert.Equal(100, lines.Length);
            Assert.Equal(100, lines.Select(line => line[(line.LastIndexOf(": ", StringComparison.Ordinal) + 2)..]).Distinct().Count());
            Assert.All(lines, line => Assert.Contains("[Information] user-host:", line));
            using var reopened = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
}
