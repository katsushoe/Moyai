using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Moyai.Application.Authentication;
using Moyai.Infrastructure.Authentication;

namespace Moyai.Infrastructure.Tests.Authentication;

public sealed class AssertionProtocolTests
{
    [Fact]
    public async Task ValidateValidAssertionReturnsPrincipalAndRejectsReplayAfterRestart()
    {
        await using var f = new AssertionFixture();
        await f.InitializeAsync();
        SignedAssertion assertion = await f.Issuer.IssueAsync(f.Context);
        AssertionPrincipal principal = await f.Validator().ValidateAsync(assertion.Value, f.Context);
        Assert.Equal(f.Context, principal.Context);
        ProviderAuthenticationException failure = await Assert.ThrowsAsync<ProviderAuthenticationException>(() => f.Validator().ValidateAsync(assertion.Value, f.Context));
        Assert.Equal("auth_replay_detected", failure.Code);
        Assert.DoesNotContain(assertion.Value, JsonSerializer.Serialize(assertion), StringComparison.Ordinal);
        Assert.DoesNotContain(assertion.Value, assertion.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("aud", "buckettie", "auth_audience_mismatch")]
    [InlineData("prv", "buckettie", "auth_audience_mismatch")]
    [InlineData("iss", "moyai:other", "auth_assertion_invalid")]
    [InlineData("sub", "moyai:other", "auth_assertion_invalid")]
    [InlineData("repository", "github.com/other/repo", "auth_project_mismatch")]
    [InlineData("project", "00000000-0000-0000-0000-000000000001", "auth_project_mismatch")]
    [InlineData("operation_id", "other-operation", "auth_project_mismatch")]
    [InlineData("protocol_version", "2", "auth_protocol_unsupported")]
    [InlineData("jti", "short", "auth_assertion_invalid")]
    public async Task ValidateSignedClaimMismatchRejectsIndividually(string claim, string value, string code)
    {
        await using var f = new AssertionFixture();
        await f.InitializeAsync();
        string assertion = await RewriteAsync(f, p => p[claim] = value);
        ProviderAuthenticationException error = await Assert.ThrowsAsync<ProviderAuthenticationException>(() => f.Validator().ValidateAsync(assertion, f.Context));
        Assert.Equal(code, error.Code);
    }

    [Theory]
    [InlineData("iss")]
    [InlineData("sub")]
    [InlineData("aud")]
    [InlineData("iat")]
    [InlineData("nbf")]
    [InlineData("exp")]
    [InlineData("jti")]
    [InlineData("prv")]
    [InlineData("project")]
    [InlineData("repository")]
    [InlineData("scope")]
    [InlineData("protocol_version")]
    [InlineData("operation_id")]
    public async Task ValidateMissingRequiredClaimRejects(string claim)
    {
        await using var f = new AssertionFixture();
        await f.InitializeAsync();
        string assertion = await RewriteAsync(f, p => p.Remove(claim));
        await Assert.ThrowsAsync<ProviderAuthenticationException>(() => f.Validator().ValidateAsync(assertion, f.Context));
    }

    [Theory]
    [InlineData(149, true)]
    [InlineData(150, false)]
    [InlineData(-30, true)]
    [InlineData(-31, false)]
    public async Task ValidateTimeBoundaryEnforcesSkew(int offset, bool accepted)
    {
        await using var f = new AssertionFixture();
        await f.InitializeAsync();
        // Trust validity starts earlier than the clock-skew test range.
        f.Clock.Now = f.Clock.Now.AddMinutes(5);
        SignedAssertion assertion = await f.Issuer.IssueAsync(f.Context);
        f.Clock.Now = f.Clock.Now.AddSeconds(offset);
        if (accepted) Assert.NotNull(await f.Validator().ValidateAsync(assertion.Value, f.Context));
        else await Assert.ThrowsAsync<ProviderAuthenticationException>(() => f.Validator().ValidateAsync(assertion.Value, f.Context));
    }

    [Fact]
    public async Task ValidateConcurrentReplayOnlyOneSucceeds()
    {
        await using var f = new AssertionFixture();
        await f.InitializeAsync();
        SignedAssertion assertion = await f.Issuer.IssueAsync(f.Context);
        Task<bool>[] tasks = Enumerable.Range(0, 8).Select(async _ =>
        {
            try { await f.Validator().ValidateAsync(assertion.Value, f.Context); return true; }
            catch (ProviderAuthenticationException error) when (error.Code == "auth_replay_detected") { return false; }
        }).ToArray();
        Assert.Equal(1, (await Task.WhenAll(tasks)).Count(static result => result));
    }

    [Fact]
    public async Task ValidateArrayAudienceAndExcessScopeRejects()
    {
        await using var f = new AssertionFixture();
        await f.InitializeAsync();
        string audience = await RewriteAsync(f, p => p["aud"] = new JsonArray("githubie"));
        string scope = await RewriteAsync(f, p => p["scope"] = new JsonArray("repository.push", "release.publish"));
        await Assert.ThrowsAsync<ProviderAuthenticationException>(() => f.Validator().ValidateAsync(audience, f.Context));
        ProviderAuthenticationException error = await Assert.ThrowsAsync<ProviderAuthenticationException>(() => f.Validator().ValidateAsync(scope, f.Context));
        Assert.Equal("auth_scope_denied", error.Code);
    }

    [Theory]
    [InlineData("githubie", "buckettie")]
    [InlineData("buckettie", "githubie")]
    public async Task ValidateDifferentProviderRejects(string sender, string recipient)
    {
        await using var f = new AssertionFixture();
        await f.InitializeAsync();
        SignedAssertion assertion = await f.Issuer.IssueAsync(f.Context with { Provider = sender });
        AssertionContext expected = f.Context with { Provider = recipient };
        ProviderAuthenticationException error = await Assert.ThrowsAsync<ProviderAuthenticationException>(() => f.Validator(expected).ValidateAsync(assertion.Value, expected));
        Assert.Equal("auth_audience_mismatch", error.Code);
    }

    [Theory]
    [InlineData("none")]
    [InlineData("HS256")]
    [InlineData("ES384")]
    public async Task ValidateWrongAlgorithmRejects(string algorithm)
    {
        await using var f = new AssertionFixture();
        await f.InitializeAsync();
        SignedAssertion assertion = await f.Issuer.IssueAsync(f.Context);
        string[] parts = assertion.Value.Split('.');
        JsonObject h = JsonNode.Parse(AssertionJson.Decode(parts[0]))!.AsObject();
        h["alg"] = algorithm;
        parts[0] = AssertionJson.Encode(JsonSerializer.SerializeToUtf8Bytes(h));
        await Assert.ThrowsAsync<ProviderAuthenticationException>(() => f.Validator().ValidateAsync(string.Join('.', parts), f.Context));
    }

    internal static async Task<string> RewriteAsync(AssertionFixture f, Action<JsonObject> change)
    {
        SignedAssertion original = await f.Issuer.IssueAsync(f.Context);
        string[] parts = original.Value.Split('.');
        JsonObject payload = JsonNode.Parse(AssertionJson.Decode(parts[1]))!.AsObject();
        change(payload);
        string input = parts[0] + "." + AssertionJson.Encode(JsonSerializer.SerializeToUtf8Bytes(payload));
        byte[] signature = await f.Ring.SignAsync(original.KeyId, Encoding.ASCII.GetBytes(input));
        return input + "." + AssertionJson.Encode(signature);
    }
}
