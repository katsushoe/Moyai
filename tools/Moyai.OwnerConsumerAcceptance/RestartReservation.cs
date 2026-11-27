using System.Security.Cryptography;
using System.Text;

namespace Moyai.OwnerConsumerAcceptance;

/// <summary>Reserves a case once and prevents concurrent Consumer processes.</summary>
internal static class RestartReservation
{
    internal static FileStream Reserve(string profile, string caseId)
    {
        if (!Path.IsPathFullyQualified(profile) || !Directory.Exists(profile) || string.IsNullOrWhiteSpace(caseId))
            throw new InvalidOperationException("Invalid dedicated profile or case.");
        var exclusive = new FileStream(Path.Combine(profile, "restart-consumer.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        try
        {
            if (File.Exists(Path.Combine(profile, "mutation-pending.json")))
                throw new InvalidOperationException("A previous mutation is pending reconciliation.");
            string name = "restart-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(caseId))) + ".started";
            using (new FileStream(Path.Combine(profile, name), FileMode.CreateNew, FileAccess.Write, FileShare.None)) { }
            return exclusive;
        }
        catch
        {
            exclusive.Dispose();
            throw;
        }
    }
}
