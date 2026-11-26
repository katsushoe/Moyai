using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using Moyai.Configuration;

namespace Moyai.Mcp.Tests;

public sealed class UserTokenAuthenticationTests
{
    [Theory]
    [InlineData(null, false)]
    [InlineData("wrong", false)]
    [InlineData("Basic ignored", false)]
    [InlineData("Bearer BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB", false)]
    [InlineData("Bearer AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA", true)]
    [InlineData("bearer AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA", true)]
    public async Task OnlyProfileTokenIsAccepted(string? header, bool accepted)
    {
        var context = new DefaultHttpContext();
        if (header is not null) context.Request.Headers.Authorization = header;
        bool called = false;
        var middleware = new UserTokenAuthentication(_ => { called = true; return Task.CompletedTask; }, new string('A', 64));
        await middleware.InvokeAsync(context);
        Assert.Equal(accepted, called);
        Assert.Equal(accepted ? 200 : 401, context.Response.StatusCode);
        if (!accepted) Assert.Equal("Bearer", context.Response.Headers.WWWAuthenticate.ToString());
    }

    [Fact]
    public async Task MultipleCredentialsAreRejected()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers.Authorization = new StringValues(["Bearer " + new string('A', 64), "Bearer " + new string('B', 64)]);
        var middleware = new UserTokenAuthentication(_ => throw new InvalidOperationException("Unexpected request"), new string('A', 64));
        await middleware.InvokeAsync(context);
        Assert.Equal(401, context.Response.StatusCode);
    }

    [Theory]
    [InlineData("")]
    [InlineData("short")]
    [InlineData("GGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGG")]
    public void InvalidProfileSecretFailsClosed(string token) => Assert.Throws<InvalidOperationException>(() => new MoyaiSettings { ApiToken = token }.Validate());
}
