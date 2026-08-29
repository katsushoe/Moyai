using Moyai.Domain.Releases;

namespace Moyai.Application.Releases;

public sealed record CreateReleaseCommand(string Project, string Version, ReleaseChannel Channel, string? ReleaseNotes, string ActorType, string ActorName);
public sealed record UpdateReleaseCommand(string Project, string Version, ReleaseChannel Channel, string? TagName, string? CommitHash, string? ReleaseNotes, DateTimeOffset? PlannedAt, long ExpectedRevision, string ActorType, string ActorName);
public sealed record TransitionReleaseCommand(string Project, string Version, ReleaseStatus NextStatus, long ExpectedRevision, string ActorType, string ActorName);
