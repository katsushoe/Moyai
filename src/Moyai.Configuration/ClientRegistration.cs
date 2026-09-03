using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Tomlyn;
using Tomlyn.Model;
using Tomlyn.Serialization;

namespace Moyai.Configuration;

/// <summary>Owns only the Moyai entry in a selected user's MCP configuration.</summary>
public sealed class ClientRegistration
{
    private readonly string _client;
    private readonly string _configPath;
    private readonly string _ownerPath;
    private readonly string _journalPath;
    private readonly string _stateDirectory;

    /// <summary>Selects an existing user profile, without using the installer's service identity.</summary>
    public ClientRegistration(string clientName, string profile)
    {
        if (clientName is not ("codex" or "claude")) throw new ArgumentException("Client must be codex or claude.");
        if (!Path.IsPathFullyQualified(profile) || !Directory.Exists(profile))
            throw new ArgumentException("An existing absolute user profile directory is required.");
        _client = clientName;
        profile = Path.GetFullPath(profile);
        _configPath = _client == "codex" ? Path.Combine(profile, ".codex", "config.toml") : Path.Combine(profile, ".claude.json");
        _stateDirectory = Path.Combine(profile, ".moyai");
        _ownerPath = Path.Combine(_stateDirectory, _client + "-owner.json");
        _journalPath = Path.Combine(_stateDirectory, _client + "-pending.json");
        foreach (string path in new[] { _configPath, _ownerPath, _journalPath }) RejectLinks(path);
    }

    /// <summary>Atomically registers or removes an owned entry; optionally retains an MSI rollback journal.</summary>
    public string Apply(bool configure, string? endpoint, bool transaction = false, string transactionId = "manual")
    {
        if (configure) MoyaiSettings.ValidateUrl(endpoint ?? throw new ArgumentNullException(nameof(endpoint)));
        Directory.CreateDirectory(_stateDirectory);
        using var gate = new FileStream(Path.Combine(_stateDirectory, _client + ".lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        if (File.Exists(_journalPath)) throw new InvalidOperationException("Pending client transaction; run client-transaction with --phase rollback or commit first.");
        byte[]? original = Read(_configPath);
        byte[]? owner = Read(_ownerPath);
        string? ownedUrl = owner is null ? null : JsonSerializer.Deserialize<string>(owner);
        string text = original is null ? "" : Encoding.UTF8.GetString(original).TrimStart('\uFEFF');
        (string updated, string status) = _client == "codex"
            ? EditToml(text, configure, endpoint, ownedUrl)
            : EditJson(text, configure, endpoint, ownedUrl);
        if (status != "changed") return status;
        byte[] after = Encoding.UTF8.GetBytes(updated);
        byte[]? nextOwner = configure ? JsonSerializer.SerializeToUtf8Bytes(endpoint) : null;
        var journal = new Journal(original, after, owner, nextOwner, transactionId);
        WriteAtomic(_journalPath, JsonSerializer.SerializeToUtf8Bytes(journal));
        try
        {
            ReplaceChecked(_configPath, original, after);
            ReplaceChecked(_ownerPath, owner, nextOwner);
        }
        catch
        {
            Restore(journal);
            File.Delete(_journalPath);
            throw;
        }
        if (!transaction) File.Delete(_journalPath);
        return configure ? "configured" : "unconfigured";
    }

    /// <summary>Completes or rolls back an MSI transaction. Missing journals are harmless.</summary>
    public void Finish(string phase, string? transactionId = null)
    {
        if (phase is not ("rollback" or "commit")) throw new ArgumentException("Phase must be rollback or commit.");
        if (!File.Exists(_journalPath)) return;
        using var gate = new FileStream(Path.Combine(_stateDirectory, _client + ".lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        var journal = JsonSerializer.Deserialize<Journal>(File.ReadAllBytes(_journalPath))
            ?? throw new InvalidOperationException("Invalid client transaction journal.");
        if (transactionId is not null && !StringComparer.Ordinal.Equals(transactionId, journal.TransactionId)) return;
        if (phase == "rollback") Restore(journal);
        File.Delete(_journalPath);
    }

    private void Restore(Journal journal)
    {
        RestoreFile(_configPath, journal.Before, journal.After);
        RestoreFile(_ownerPath, journal.OwnerBefore, journal.OwnerAfter);
    }

    private static void RestoreFile(string path, byte[]? before, byte[]? after)
    {
        if (Equal(Read(path), before)) return;
        ReplaceChecked(path, after, before);
    }

    private static (string, string) EditToml(string text, bool configure, string? endpoint, string? ownedUrl)
    {
        TomlTable root;
        var options = new TomlSerializerOptions { MetadataStore = new TomlMetadataStore() };
        try { root = TomlSerializer.Deserialize<TomlTable>(text, options) ?? new TomlTable(); }
        catch (Exception exception) when (exception.GetType().Namespace?.StartsWith("Tomlyn", StringComparison.Ordinal) is true)
        { throw new InvalidOperationException("Invalid Codex TOML; configuration was not changed."); }
        if (root.TryGetValue("mcp_servers", out object? value) && value is not TomlTable)
            throw new InvalidOperationException("Codex mcp_servers must be a table.");
        var servers = value as TomlTable ?? new TomlTable();
        servers.TryGetValue("moyai", out object? existing);
        bool Matches(string? url) => existing is TomlTable entry && entry.Count == 1 && entry.TryGetValue("url", out object? actual) && Equals(actual, url);
        string status = Decide(configure, existing is not null, Matches(endpoint), Matches(ownedUrl), ownedUrl);
        if (status != "changed") return (text, status);
        if (configure) servers["moyai"] = new TomlTable { ["url"] = endpoint! };
        else servers.Remove("moyai");
        root["mcp_servers"] = servers;
        string result = TomlSerializer.Serialize(root, options);
        try { _ = TomlSerializer.Deserialize<TomlTable>(result); }
        catch (Exception exception) when (exception.GetType().Namespace?.StartsWith("Tomlyn", StringComparison.Ordinal) is true)
        { throw new InvalidOperationException("Generated Codex configuration is invalid."); }
        return (result, status);
    }

    private static (string, string) EditJson(string text, bool configure, string? endpoint, string? ownedUrl)
    {
        JsonObject root;
        try { root = text.Length == 0 ? new JsonObject() : JsonNode.Parse(text) as JsonObject ?? throw new JsonException(); }
        catch (JsonException) { throw new InvalidOperationException("Invalid Claude JSON; configuration was not changed."); }
        if (root.TryGetPropertyValue("mcpServers", out JsonNode? value) && value is not JsonObject)
            throw new InvalidOperationException("Claude mcpServers must be an object.");
        var servers = value as JsonObject ?? new JsonObject();
        servers.TryGetPropertyValue("moyai", out JsonNode? existing);
        bool Matches(string? url) => existing is JsonObject entry && entry.Count == 2 &&
            JsonNode.DeepEquals(entry["type"], JsonValue.Create("http")) && JsonNode.DeepEquals(entry["url"], JsonValue.Create(url));
        string status = Decide(configure, existing is not null, Matches(endpoint), Matches(ownedUrl), ownedUrl);
        if (status != "changed") return (text, status);
        if (configure) servers["moyai"] = new JsonObject { ["type"] = "http", ["url"] = endpoint };
        else servers.Remove("moyai");
        if (value is null) root["mcpServers"] = servers;
        return (root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n", status);
    }

    private static string Decide(bool configure, bool exists, bool same, bool ownedMatches, string? ownedUrl)
    {
        if (!configure && ownedUrl is null) return "not_owned";
        if (!exists) return configure ? "changed" : "absent";
        if (configure && same) return "unchanged";
        if (ownedUrl is null || !ownedMatches)
            throw new InvalidOperationException("Existing Moyai entry is not owned or was modified; configuration was not changed.");
        return "changed";
    }

    private static byte[]? Read(string path) => File.Exists(path) ? File.ReadAllBytes(path) : null;
    private static bool Equal(byte[]? left, byte[]? right) => left is null ? right is null : right is not null && left.AsSpan().SequenceEqual(right);

    private static void ReplaceChecked(string path, byte[]? expected, byte[]? replacement)
    {
        RejectLinks(path);
        if (!Equal(Read(path), expected)) throw new IOException("Client configuration changed concurrently; retry after closing the client.");
        if (replacement is null) File.Delete(path);
        else WriteAtomic(path, replacement);
    }

    private static void WriteAtomic(string path, byte[] bytes)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(bytes);
                stream.Flush(true);
            }
            if (File.Exists(path)) File.Replace(temporary, path, null);
            else File.Move(temporary, path);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private static void RejectLinks(string path)
    {
        for (string? current = Path.GetFullPath(path); current is not null; current = Path.GetDirectoryName(current))
            if (Path.Exists(current) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Client configuration paths must not traverse symbolic links or junctions.");
    }

    private sealed record Journal(byte[]? Before, byte[] After, byte[]? OwnerBefore, byte[]? OwnerAfter, string TransactionId);
}
