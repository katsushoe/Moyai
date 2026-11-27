using Moyai.Application.Projects;
using Moyai.Application.Providers;
using Moyai.Infrastructure.Persistence;
using Moyai.Infrastructure.Tests.Authentication;

namespace Moyai.Infrastructure.Tests.Providers;

public sealed class PublicationRoutingTests
{
    private const string Id = "AAAAAAAA-AAAA-4AAA-8AAA-AAAAAAAAAAAA";
    private static readonly string Sha = new('a',40);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExpiredAssertionDoesNotReplayAndRetainsOriginalOperationId(bool owner)
    {
        await using var fixture = new AssertionFixture();
        await fixture.InitializeAsync();
        var projects=new SqliteProjectRepository(fixture.Database);
        var service=new ProjectService(projects,fixture.Clock);
        var project=await service.CreateAsync(new("display","source",RepositoryUrl:"https://github.com/example/repo",RepositoryProvider:"githubie"));
        await service.SetProviderRepositoryAsync(project.Name,project.Revision,"registered",false,"test","publication");
        var provider=new Recording(owner);
        var routing=new ProviderRoutingService(projects,new SqliteServiceTokenRepository(fixture.Database),[provider],fixture.Clock);
        var result=await routing.ExecutePublicationAsync("display",Id,new("Feature/Exact","main",Sha,"title",Id,false));
        Assert.Equal(1,provider.Calls);
        Assert.Equal(Id.ToLowerInvariant(),provider.Request!.OperationId);
        Assert.Equal("registered",provider.Request.RepositoryId);
        Assert.Equal(Id.ToLowerInvariant(),result.RequestContext.OperationId);
        Assert.False(result.Ok);
        Assert.Equal("auth_assertion_expired",result.Error!.Code);
        Assert.Equal(owner,provider.Request.UseWindowsOwnerAuthentication);
    }

    [Fact]
    public async Task InvalidOrMissingProjectDoesNotReachProvider()
    {
        await using var fixture=new AssertionFixture();
        await fixture.InitializeAsync();
        var provider=new Recording(true);
        var routing=new ProviderRoutingService(new SqliteProjectRepository(fixture.Database),
            new SqliteServiceTokenRepository(fixture.Database),[provider],fixture.Clock);
        var missing=await routing.ExecutePublicationAsync("missing",Id);
        var invalid=await routing.ExecutePublicationAsync("missing","not-a-uuid");
        Assert.Equal("project_not_found",missing.Error!.Code);
        Assert.Equal("publication_invalid",invalid.Error!.Code);
        Assert.Equal(0,provider.Calls);
    }

    [Fact]
    public async Task StatusDoesNotInventSourceRequestContext()
    {
        await using var fixture=new AssertionFixture();
        await fixture.InitializeAsync();
        var projects=new SqliteProjectRepository(fixture.Database);
        await new ProjectService(projects,fixture.Clock).CreateAsync(new("display","source",RepositoryUrl:"https://github.com/example/repo",RepositoryProvider:"githubie"));
        var provider=new Recording(true);
        var routing=new ProviderRoutingService(projects,new SqliteServiceTokenRepository(fixture.Database),[provider],fixture.Clock);
        var result=await routing.ExecutePublicationAsync("display",Id);
        Assert.Equal(RepositoryOperation.PullRequestPublishStatus,provider.Request!.Operation);
        Assert.Null(result.RequestContext.SourceBranch);
        Assert.Null(result.RequestContext.ExpectedSourceSha);
        Assert.Equal(Id.ToLowerInvariant(),result.RequestContext.OperationId);
    }

    private sealed class Recording(bool owner) : IRepositoryProvider
    {
        public string Name=>"githubie";
        public bool UsesWindowsOwnerAuthentication=>owner;
        public int Calls{get;private set;}
        public RepositoryProviderRequest? Request{get;private set;}
        public Task<RepositoryProviderResult> ExecuteAsync(RepositoryProviderRequest request,CancellationToken cancellationToken=default)
        {
            Calls++;Request=request;
            return Task.FromResult(new RepositoryProviderResult(false,request.Operation.ContractName(),null,"auth_assertion_expired","expired"));
        }
    }
}

