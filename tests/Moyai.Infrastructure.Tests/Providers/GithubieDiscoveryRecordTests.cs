using System.Text;
using System.Text.Json.Nodes;
using Moyai.Infrastructure.Providers;

namespace Moyai.Infrastructure.Tests.Providers;

public sealed class GithubieDiscoveryRecordTests
{
    private static JsonObject Record() => new()
    {
        ["schema_version"] = 1, ["provider_id"] = "githubie", ["profile_id"] = Guid.NewGuid().ToString(),
        ["owner_sid"] = "S-1-5-21-123", ["session_id"] = 1, ["process_id"] = 123,
        ["process_start_utc_ticks"] = 123456789, ["endpoint"] = "http://127.0.0.1:54321/mcp",
        ["generation"] = Guid.NewGuid().ToString("N"), ["runtime_path"] = Path.GetFullPath("Githubie.Server.exe"),
        ["runtime_sha256"] = new string('A', 64), ["profile_sha256"] = new string('B', 64),
        ["protected_credential"] = Convert.ToBase64String([1, 2, 3]),
    };

    private static GithubieDiscoveryRecord Parse(JsonObject value) => GithubieDiscoveryRecord.Parse(Encoding.UTF8.GetBytes(value.ToJsonString()));

    [Fact]
    public void ValidRecordDoesNotExposeProtectedCredential()
    {
        GithubieDiscoveryRecord result = Parse(Record());
        Assert.Equal("http://127.0.0.1:54321/mcp", result.Endpoint.AbsoluteUri);
        Assert.DoesNotContain(result.ProtectedCredential, result.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("provider_id", "buckettie", "discovery_unsupported")]
    [InlineData("generation", "not-a-guid", "discovery_invalid")]
    [InlineData("runtime_sha256", "abcd", "discovery_invalid")]
    [InlineData("protected_credential", "not-base64", "discovery_invalid")]
    [InlineData("endpoint", "http://localhost:54321/mcp", "discovery_endpoint_rejected")]
    [InlineData("endpoint", "http://127.0.0.1:54321/a/../mcp", "discovery_endpoint_rejected")]
    public void InvalidValuesFailClosed(string field, string value, string code)
    {
        JsonObject record = Record();
        record[field] = value;
        Assert.Equal(code, Assert.Throws<UserHostConnectionException>(() => Parse(record)).Code);
    }

    [Fact]
    public void MissingUnknownDuplicateAndOversizedFieldsAreRejected()
    {
        JsonObject missing = Record();
        missing.Remove("owner_sid");
        Assert.Throws<UserHostConnectionException>(() => Parse(missing));
        JsonObject extra = Record();
        extra["extra"] = true;
        Assert.Throws<UserHostConnectionException>(() => Parse(extra));
        string duplicate = Record().ToJsonString().Replace("\"schema_version\":1", "\"schema_version\":1,\"schema_version\":1");
        Assert.Throws<UserHostConnectionException>(() => GithubieDiscoveryRecord.Parse(Encoding.UTF8.GetBytes(duplicate)));
        Assert.Throws<UserHostConnectionException>(() => GithubieDiscoveryRecord.Parse(new byte[65537]));
    }
}
