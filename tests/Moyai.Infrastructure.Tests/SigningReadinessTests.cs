using Microsoft.Data.Sqlite;
using Moyai.Infrastructure.Authentication;
using Moyai.Infrastructure.Persistence;

namespace Moyai.Infrastructure.Tests;

public sealed class SigningReadinessTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), "moyai-signing-diagnostic-" + Guid.NewGuid().ToString("N") + ".db");

    [Fact]
    public async Task MissingDatabaseIsNotCreatedAndEmptyIssuerDoesNotProbe()
    {
        var options = new SqliteDatabaseOptions("Data Source=" + _path);
        var missing = new SqliteSigningReadiness(options, "issuer", "ns", "v1", TimeProvider.System);
        Assert.Equal("metadata_unavailable", (await missing.ReadAsync()).State);
        Assert.False(File.Exists(_path));
        Assert.Equal("issuer_not_configured", (await new SqliteSigningReadiness(options, "", "ns", "v1", TimeProvider.System).ReadAsync()).State);
        Assert.False(File.Exists(_path));
    }

    [Theory]
    [InlineData(-60, 60, true, "configured")]
    [InlineData(-60, 60, false, "protector_unavailable")]
    [InlineData(-120, -60, true, "active_key_expired")]
    [InlineData(60, 120, true, "active_key_not_yet_valid")]
    public async Task ReadinessReadsPublicMetadataWithoutWritingOrSigning(int start, int end, bool available, string expected)
    {
        var clock = new Authentication.TestClock();
        var connectionString = new SqliteConnectionStringBuilder { DataSource = _path, Pooling = false }.ToString();
        await using (var connection = new SqliteConnection(connectionString))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            // 最小schemaに秘密列はありません。診断がprivate keyを必要としないことも検証します。
            command.CommandText = "CREATE TABLE assertion_keys(issuer TEXT,state TEXT,not_before INTEGER,not_after INTEGER); CREATE TABLE protector_state(key_namespace TEXT,active_version TEXT); INSERT INTO assertion_keys VALUES('issuer','Active',$start,$end);";
            long now = clock.GetUtcNow().ToUnixTimeSeconds();
            command.Parameters.AddWithValue("$start", now + start);
            command.Parameters.AddWithValue("$end", now + end);
            await command.ExecuteNonQueryAsync();
        }
        byte[] before = await File.ReadAllBytesAsync(_path);
        int probes = 0;
        var probe = new SqliteSigningReadiness(new(connectionString), "issuer", "ns", "fallback", clock,
            (version, _) => { Assert.Equal("fallback", version); probes++; return Task.FromResult(available); });
        var result = await probe.ReadAsync();
        Assert.Equal(expected, result.State);
        Assert.False(result.SigningAttempted);
        Assert.Equal(start < 0 && end > 0 ? 1 : 0, probes);
        Assert.Equal(before, await File.ReadAllBytesAsync(_path));
    }

    public void Dispose() { if (File.Exists(_path)) File.Delete(_path); }
}
