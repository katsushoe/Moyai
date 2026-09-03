using System.Text.Json;
using ModelContextProtocol.Protocol;
using Moyai.Application.Providers;
using Moyai.Infrastructure.Providers;

namespace Moyai.Infrastructure.Tests.Providers;

public sealed class RepositoryProviderResponseTests
{
    private const string Failure = "{\"ok\":false,\"error\":{\"code\":\"repository_not_found\",\"correlation_id\":\"request-1\",\"retryable\":false}}";

    [Theory]
    [InlineData(null)]
    [InlineData(false)]
    [InlineData(true)]
    public void ParseWhenBusinessFailsReturnsFailureRegardlessOfTransportFlag(bool? isError)
    {
        var response = Text(Failure, isError);

        RepositoryProviderResult result = RepositoryProviderResponse.Parse(RepositoryOperation.BranchCreate, response);

        Assert.False(result.Ok);
        Assert.Null(result.Output);
        Assert.Equal("provider_not_found", result.ErrorCode);
        Assert.Equal(Failure, result.ErrorMessage);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("{\"ok\":true}")]
    public void ParseWhenStructuredContentFailsPreservesFailure(string? text)
    {
        var response = Text(text);
        response.StructuredContent = JsonSerializer.Deserialize<JsonElement>(Failure);

        RepositoryProviderResult result = RepositoryProviderResponse.Parse(RepositoryOperation.Status, response);

        Assert.False(result.Ok);
        Assert.Equal("provider_not_found", result.ErrorCode);
        Assert.Contains("request-1", result.ErrorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void ParseWhenTextFailsButStructuredContentSucceedsDoesNotHideFailure()
    {
        var response = Text(Failure);
        response.StructuredContent = JsonSerializer.Deserialize<JsonElement>("{\"ok\":true}");

        RepositoryProviderResult result = RepositoryProviderResponse.Parse(RepositoryOperation.Push, response);

        Assert.False(result.Ok);
        Assert.Equal(Failure, result.ErrorMessage);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("42")]
    [InlineData("{}")]
    [InlineData("{\"ok\":null}")]
    [InlineData("{\"ok\":\"false\"}")]
    public void ParseWhenRepositoryResultIsInvalidFailsClosed(string? payload)
    {
        RepositoryProviderResult result = RepositoryProviderResponse.Parse(RepositoryOperation.Status, Text(payload));

        Assert.False(result.Ok);
        Assert.Equal("provider_invalid_response", result.ErrorCode);
    }

    [Theory]
    [InlineData("Unauthorized token", "provider_authentication_failed")]
    [InlineData("42", "provider_operation_failed")]
    [InlineData("[]", "provider_operation_failed")]
    [InlineData("null", "provider_operation_failed")]
    [InlineData("{\"error\":{\"code\":42}}", "provider_operation_failed")]
    public void ParseWhenTransportFailsHandlesNonEnvelopeErrors(string payload, string code)
    {
        RepositoryProviderResult result = RepositoryProviderResponse.Parse(RepositoryOperation.Status, Text(payload, true));

        Assert.False(result.Ok);
        Assert.Equal(code, result.ErrorCode);
    }

    [Theory]
    [InlineData(RepositoryOperation.Status, "{\"ok\":true,\"data\":{\"ahead\":2}}")]
    [InlineData(RepositoryOperation.ProviderVersion, "{\"name\":\"Provider\",\"version\":\"1.0\"}")]
    [InlineData(RepositoryOperation.ProviderVersion, "\"1.0\"")]
    [InlineData(RepositoryOperation.ProviderCapabilities, "{\"operations\":[\"status\"]}")]
    public void ParseWhenSuccessfulPreservesPayload(RepositoryOperation operation, string payload)
    {
        RepositoryProviderResult result = RepositoryProviderResponse.Parse(operation, Text(payload));

        Assert.True(result.Ok);
        Assert.Equal(payload, result.Output);
        Assert.Null(result.ErrorCode);
    }

    [Fact]
    public void ParseWhenRetryablePreservesProviderDetails()
    {
        const string payload = "{\"ok\":false,\"error\":{\"code\":\"rate_limited\",\"retryable\":true,\"retry_after_seconds\":60}}";

        RepositoryProviderResult result = RepositoryProviderResponse.Parse(RepositoryOperation.Pull, Text(payload));

        Assert.Equal("provider_retryable_failure", result.ErrorCode);
        Assert.Equal(payload, result.ErrorMessage);
    }

    private static CallToolResult Text(string? text, bool? isError = false) => new()
    {
        IsError = isError,
        Content = text is null ? [] : [new TextContentBlock { Text = text }],
    };
}
