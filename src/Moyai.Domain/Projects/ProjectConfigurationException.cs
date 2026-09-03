namespace Moyai.Domain.Projects;

/// <summary>Expected execution-setting failure that is safe to report to callers.</summary>
public sealed class ProjectConfigurationException(string message) : InvalidOperationException(message);
