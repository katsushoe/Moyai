namespace Moyai.Application.Providers;

public sealed partial class ProviderRoutingService
{
    /// <summary>明示UUIDを保持し、変更要求を自動再送しません。</summary>
    public async Task<PullRequestPublicationResult> ExecutePublicationAsync(string projectName, string operationId,
        PullRequestPublicationInput? input = null, CancellationToken cancellationToken = default)
    {
        RepositoryOperation operation = input is null ? RepositoryOperation.PullRequestPublishStatus : RepositoryOperation.PullRequestPublishCreate;
        var context = new PublicationRequestContext(operationId, input?.SourceBranch, input?.DestinationBranch, input?.ExpectedSourceSha?.ToLowerInvariant());
        try
        {
            PullRequestPublicationInput.ValidateOperationId(operationId);
            input?.Validate();
            if (input is not null && Guid.Parse(input.OperationId) != Guid.Parse(operationId)) throw new ArgumentException("Operation mismatch.");
            context = context with { OperationId = Guid.Parse(operationId).ToString("D") };
        }
        catch (ArgumentException) { return PullRequestPublicationResult.Failure(projectName, null, operation, context, "publication_invalid"); }
        var project = await _projects.GetByNameAsync(projectName, cancellationToken).ConfigureAwait(false);
        if (project is null) return PullRequestPublicationResult.Failure(projectName, null, operation, context, "project_not_found");
        string provider = ProviderName(project.RepositoryProvider);
        RepositoryProviderResult result = await ExecuteAsync(projectName, operation, publication: input,
            publicationOperationId: context.OperationId, cancellationToken: cancellationToken).ConfigureAwait(false);
        return PullRequestPublicationResult.Parse(projectName, provider is "githubbie" ? "githubie" : provider, operation, context, result);
    }
}

