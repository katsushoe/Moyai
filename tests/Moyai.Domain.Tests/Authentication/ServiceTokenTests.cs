using Moyai.Domain.Authentication;

namespace Moyai.Domain.Tests.Authentication;

public sealed class ServiceTokenTests
{
    [Fact]
    public void IssueCreatesDistinct256BitTokens()
    {
        ServiceToken first = ServiceToken.Issue("githubbie", ["repository.write"], null, TimeProvider.System);
        ServiceToken second = ServiceToken.Issue("githubbie", ["repository.write"], null, TimeProvider.System);
        Assert.Equal(32, Convert.FromBase64String(first.Token).Length);
        Assert.NotEqual(first.Token, second.Token);
    }

    [Theory]
    [InlineData("githubbie", "repository.write", true)]
    [InlineData("buckettie", "repository.write", false)]
    [InlineData("githubbie", "release.write", false)]
    public void IntrospectValidatesAudienceAndScope(string audience, string scope, bool expected)
    {
        ServiceToken token = ServiceToken.Issue("githubbie", ["repository.write"], null, TimeProvider.System);
        Assert.Equal(expected, token.Introspect(audience, scope, TimeProvider.System));
    }
}
