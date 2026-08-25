namespace Moyai.Application.WorkItems;

public sealed class WorkItemNotFoundException(string key) : InvalidOperationException($"Work item '{key}' was not found.");
