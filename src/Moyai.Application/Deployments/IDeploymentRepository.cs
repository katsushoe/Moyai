using Moyai.Domain.Deployments;
using Moyai.Domain.Events;

namespace Moyai.Application.Deployments;

public interface IDeploymentRepository
{
    Task<DeploymentTarget?> GetTargetAsync(Guid projectId, CancellationToken cancellationToken = default);
    Task UpsertTargetAsync(DeploymentTarget target, long expectedRevision, ProjectEvent projectEvent, CancellationToken cancellationToken = default);
    Task AddAsync(Deployment deployment, ProjectEvent projectEvent, CancellationToken cancellationToken = default);
    Task UpdateAsync(Deployment deployment, long expectedRevision, ProjectEvent projectEvent, CancellationToken cancellationToken = default);
    Task<Deployment?> GetAsync(Guid projectId, Guid deploymentId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Deployment>> ListAsync(Guid projectId, CancellationToken cancellationToken = default);
}
