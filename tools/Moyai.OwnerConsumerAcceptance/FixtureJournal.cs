using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Moyai.OwnerConsumerAcceptance;

internal sealed class FixtureJournal(string profile)
{
    private string PendingPath => Path.Combine(profile, "mutation-pending.json");
    private static string Name(string id) => "case-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(id)));
    internal string EvidencePath(string id) => Path.Combine(profile, Name(id) + ".result.json");

    internal void Reserve(string id)
    {
        if (File.Exists(PendingPath)) throw new InvalidOperationException("Reconcile the previous case before any further case.");
        using (FileStream marker = new(Path.Combine(profile, Name(id) + ".started"), FileMode.CreateNew, FileAccess.Write, FileShare.None)) { }
        using FileStream pending = new(PendingPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        JsonSerializer.Serialize(pending, new { caseId = id });
    }

    internal void Complete(string id)
    {
        using JsonDocument pending = JsonDocument.Parse(File.ReadAllText(PendingPath));
        if (pending.RootElement.GetProperty("caseId").GetString() != id) throw new InvalidOperationException("Pending case identity mismatch.");
        File.Delete(PendingPath);
    }

    internal void RequirePending(string id)
    {
        using JsonDocument pending = JsonDocument.Parse(File.ReadAllText(PendingPath));
        if (pending.RootElement.GetProperty("caseId").GetString() != id) throw new InvalidOperationException("Pending case identity mismatch.");
    }
}
