using System.Security.Cryptography;
using System.Text.Json;
using Moyai.OwnerConsumerAcceptance;

namespace Moyai.Infrastructure.Tests.Providers;

public sealed class RestartCandidateManifestTests
{
    [Fact]
    public void ExactCandidateAndPinsPass()
    {
        using var fixture = new Candidate();
        fixture.Verify();
    }

    [Fact]
    public void ModifiedPayloadIsRejected()
    {
        using var fixture = new Candidate();
        File.WriteAllText(fixture.ConsolePath, "tampered");
        Assert.Throws<InvalidOperationException>(fixture.Verify);
    }

    [Fact]
    public void ExtraPayloadFileIsRejected()
    {
        using var fixture = new Candidate();
        File.WriteAllText(Path.Combine(fixture.Root, "extra.txt"), "extra");
        Assert.Throws<InvalidOperationException>(fixture.Verify);
    }

    [Fact]
    public void ManifestChangeIsRejectedBeforePayloadValidation()
    {
        using var fixture = new Candidate();
        File.WriteAllText(fixture.Manifest, "[]");
        Assert.Throws<InvalidOperationException>(fixture.Verify);
    }

    [Theory]
    [InlineData("../outside.txt")]
    [InlineData("nested/../console.exe")]
    [InlineData("nested//console.exe")]
    public void EscapingOrNonCanonicalPathsAreRejected(string path)
    {
        using var fixture = new Candidate();
        fixture.WriteManifest(path);
        Assert.Throws<InvalidOperationException>(fixture.Verify);
    }

    private sealed class Candidate : IDisposable
    {
        private readonly string _directory = Path.Combine(Path.GetTempPath(), "moyai-candidate-test-" + Guid.NewGuid().ToString("N"));
        private string _manifestHash = "";
        private string _consoleHash = "";
        public string Root => Path.Combine(_directory, "candidate");
        public string Manifest => Path.Combine(_directory, "manifest.json");
        public string ConsolePath => Path.Combine(Root, "console.exe");

        public Candidate()
        {
            Directory.CreateDirectory(Root);
            File.WriteAllText(ConsolePath, "fixture executable");
            _consoleHash = Hash(ConsolePath);
            WriteManifest("console.exe");
        }

        public void WriteManifest(string path)
        {
            File.WriteAllText(Manifest, JsonSerializer.Serialize(new[] { new { path, bytes = new FileInfo(ConsolePath).Length, sha256 = _consoleHash } }));
            _manifestHash = Hash(Manifest);
        }

        public void Verify() => RestartCandidateManifest.Verify(Root, Manifest, _manifestHash, 1, "console.exe", _consoleHash);
        public void Dispose() => Directory.Delete(_directory, true);
        private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
    }
}
