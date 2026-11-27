using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Moyai.Configuration;

namespace Moyai.Tray;

internal sealed class SettingsFile
{
    private readonly string _path;
    private readonly byte[] _originalHash;

    internal SettingsFile(string path)
    {
        _path = path;
        byte[] bytes = File.ReadAllBytes(path);
        _originalHash = SHA256.HashData(bytes);
        var settings = Parse(bytes).Deserialize<MoyaiSettings>(MoyaiSettings.JsonOptions)
            ?? throw new InvalidDataException("Moyai settings are missing.");
        settings.Validate();
        AutoUpdate = settings.AutoUpdateOnStartup;
        Timeout = settings.RequestTimeoutSeconds;
    }

    internal bool AutoUpdate { get; }
    internal int Timeout { get; }

    internal void Save(bool autoUpdate, int timeout)
    {
        if (timeout is < 1 or > 3600) throw new ArgumentOutOfRangeException(nameof(timeout));
        if (autoUpdate == AutoUpdate && timeout == Timeout) return;
        using var mutex = new Mutex(false, @"Local\Akatsukisoft.Moyai.Settings");
        bool acquired = false;
        try
        {
            try
            {
                acquired = mutex.WaitOne(0);
            }
            catch (AbandonedMutexException)
            {
                acquired = true;
                System.Diagnostics.Trace.TraceWarning("Moyai recovered the settings mutex.");
            }
            if (!acquired) throw new IOException("Settings are being saved.");
            byte[] bytes = File.ReadAllBytes(_path);
            if (!SHA256.HashData(bytes).SequenceEqual(_originalHash))
                throw new IOException("Settings changed while the window was open.");
            JsonObject document = Parse(bytes);
            document["autoUpdateOnStartup"] = autoUpdate;
            document["requestTimeoutSeconds"] = timeout;
            string temporary = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    JsonSerializer.Serialize(stream, document, MoyaiSettings.JsonOptions);
                    stream.Flush(flushToDisk: true);
                }
                File.Replace(temporary, _path, null);
            }
            finally { File.Delete(temporary); }
        }
        finally
        {
            if (acquired) mutex.ReleaseMutex();
        }
    }

    private static JsonObject Parse(byte[] bytes)
    {
        using var reader = new StreamReader(new MemoryStream(bytes, writable: false));
        return JsonNode.Parse(reader.ReadToEnd()) as JsonObject
            ?? throw new InvalidDataException("Moyai settings must be a JSON object.");
    }
}
