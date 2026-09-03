using System.Globalization;
using System.Text.Json;
using ModelContextProtocol.Protocol;
using Moyai.Cli;

namespace Moyai.Mcp.Tests;

public sealed class CliResponseTests
{
    [Theory]
    [InlineData(null)]
    [InlineData(false)]
    [InlineData(true)]
    public void WriteWhenBusinessFailsReturnsOneAndOnlyWritesError(bool? isError)
    {
        const string payload = "{\"ok\":false,\"errorCode\":\"provider_not_found\",\"errorMessage\":\"repository missing\"}";
        var response = Text(payload, isError);
        using var output = new StringWriter(CultureInfo.InvariantCulture);
        using var error = new StringWriter(CultureInfo.InvariantCulture);

        int exitCode = CliResponse.Write("repository-status", response, output, error);

        Assert.Equal(1, exitCode);
        Assert.Equal("", output.ToString());
        using JsonDocument json = JsonDocument.Parse(error.ToString());
        Assert.False(json.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("repository-status", json.RootElement.GetProperty("command").GetString());
        Assert.Equal(payload, json.RootElement.GetProperty("error").GetProperty("message").GetString());
    }

    [Theory]
    [InlineData("{\"ok\":false}", null)]
    [InlineData("{\"ok\":false}", "{\"ok\":true}")]
    [InlineData("{\"ok\":true}", "{\"ok\":false}")]
    public void WriteWhenEitherRepresentationFailsReturnsOne(string structured, string? text)
    {
        var response = Text(text);
        response.StructuredContent = JsonSerializer.Deserialize<JsonElement>(structured);
        using var output = new StringWriter(CultureInfo.InvariantCulture);
        using var error = new StringWriter(CultureInfo.InvariantCulture);

        Assert.Equal(1, CliResponse.Write("repository-status", response, output, error));
        Assert.Equal("", output.ToString());
        using JsonDocument json = JsonDocument.Parse(error.ToString());
        Assert.False(json.RootElement.GetProperty("ok").GetBoolean());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("{\"ok\":\"false\"}")]
    [InlineData("{\"ok\":null}")]
    public void WriteWhenResponseIsInvalidReturnsStructuredError(string? payload)
    {
        using var output = new StringWriter(CultureInfo.InvariantCulture);
        using var error = new StringWriter(CultureInfo.InvariantCulture);

        Assert.Equal(1, CliResponse.Write("test", Text(payload), output, error));
        Assert.Equal("", output.ToString());
        using JsonDocument json = JsonDocument.Parse(error.ToString());
        Assert.Equal("service_invalid_response", json.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    [Theory]
    [InlineData("{\"ok\":true,\"output\":\"done\"}")]
    [InlineData("{\"name\":\"Moyai\",\"version\":\"1.2.0\"}")]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("false")]
    [InlineData("{\"id\":\"project-1\"}")]
    public void WriteWhenSuccessfulPreservesNonEnvelopeResults(string payload)
    {
        using var output = new StringWriter(CultureInfo.InvariantCulture);
        using var error = new StringWriter(CultureInfo.InvariantCulture);

        Assert.Equal(0, CliResponse.Write("test", Text(payload), output, error));
        Assert.Equal(payload + Environment.NewLine, output.ToString());
        Assert.Equal("", error.ToString());
    }

    [Fact]
    public void WriteWhenOnlyStructuredSuccessExistsOutputsJson()
    {
        var response = new CallToolResult { StructuredContent = JsonSerializer.Deserialize<JsonElement>("{\"ok\":true}") };
        using var output = new StringWriter(CultureInfo.InvariantCulture);
        using var error = new StringWriter(CultureInfo.InvariantCulture);

        Assert.Equal(0, CliResponse.Write("test", response, output, error));
        using JsonDocument json = JsonDocument.Parse(output.ToString());
        Assert.True(json.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("", error.ToString());
    }

    [Theory]
    [InlineData("{\"ok\":true}")]
    [InlineData("plain error")]
    public void WriteWhenTransportFailsDoesNotReturnSuccess(string payload)
    {
        using var output = new StringWriter(CultureInfo.InvariantCulture);
        using var error = new StringWriter(CultureInfo.InvariantCulture);

        Assert.Equal(1, CliResponse.Write("test", Text(payload, true), output, error));
        Assert.Equal("", output.ToString());
    }

    private static CallToolResult Text(string? text, bool? isError = false) => new()
    {
        IsError = isError,
        Content = text is null ? [] : [new TextContentBlock { Text = text }],
    };
}
