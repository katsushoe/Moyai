namespace Moyai.Application.Releases;

public sealed class ReleaseNotFoundException(string version) : InvalidOperationException($"Release '{version}' was not found.");
