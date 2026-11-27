using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Moyai.Configuration;

namespace Moyai.Tray;

internal sealed class StartupUpdate(HttpClient client)
{
    private static readonly Uri Feed = new("https://api.github.com/repos/katsushoe/Moyai/releases/latest");
    private const string DownloadRoot = "https://github.com/katsushoe/Moyai/releases/download/";
    private const long MaxPackageBytes = 512L * 1024 * 1024;

    internal static bool IsInstalled()
    {
        string tray = Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(MoyaiSettings.DefaultPath))!, "bin", "tray", "Moyai.Tray.exe");
        return string.Equals(Environment.ProcessPath, tray, StringComparison.OrdinalIgnoreCase);
    }

    internal async Task<bool> PrepareAsync(Version installed, bool startHost, Func<Task<bool>> canInstall, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, Feed);
        request.Headers.UserAgent.ParseAdd("Moyai-StartupUpdate/1.0");
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        using HttpResponseMessage response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound) return false;
        response.EnsureSuccessStatusCode();
        using var release = JsonDocument.Parse(await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false));
        if (Select(release.RootElement, installed) is not { } asset) return false;
        string folder = Directory.CreateTempSubdirectory("MoyaiUpdate-").FullName;
        string path = Path.Combine(folder, "Moyai.msi");
        bool handedOff = false;
        try
        {
            await DownloadAsync(asset, path, cancellationToken).ConfigureAwait(false);
            if (!await canInstall().ConfigureAwait(false)) return false;
            LaunchInstaller(path, asset.Hash, startHost);
            handedOff = true;
            return true;
        }
        finally
        {
            if (!handedOff)
            {
                File.Delete(path);
                Directory.Delete(folder);
            }
        }
    }

    internal static UpdateAsset? Select(JsonElement release, Version installed)
    {
        string? tag = release.GetProperty("tag_name").GetString();
        if (release.GetProperty("draft").GetBoolean() || release.GetProperty("prerelease").GetBoolean()
            || tag is null || !tag.StartsWith('v') || !Version.TryParse(tag[1..], out Version? version)
            || version.Revision < 0 || tag != "v" + version.ToString(4) || version <= installed) return null;
        string name = $"Moyai-{version.ToString(4)}-x64.msi";
        var candidates = release.GetProperty("assets").EnumerateArray().Where(x => x.GetProperty("name").GetString() == name).ToArray();
        if (candidates.Length != 1) return null;
        JsonElement asset = candidates[0];
        string expectedUrl = DownloadRoot + tag + "/" + name;
        if (asset.GetProperty("state").GetString() != "uploaded"
            || asset.GetProperty("browser_download_url").GetString() != expectedUrl
            || !asset.TryGetProperty("digest", out JsonElement digestElement)
            || digestElement.GetString() is not { Length: 71 } digest
            || !digest.StartsWith("sha256:", StringComparison.Ordinal)
            || !digest.AsSpan(7).ToString().All(char.IsAsciiHexDigit)
            || !asset.GetProperty("size").TryGetInt64(out long size)
            || size is <= 0 or > MaxPackageBytes) return null;
        return new(new Uri(expectedUrl), size, digest[7..].ToUpperInvariant());
    }

    private async Task DownloadAsync(UpdateAsset asset, string path, CancellationToken cancellationToken)
    {
        Uri uri = asset.Url;
        for (int redirects = 0; redirects <= 3; redirects++)
        {
            using HttpResponseMessage response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            if ((int)response.StatusCode is 301 or 302 or 303 or 307 or 308)
            {
                if (response.Headers.Location is not { IsAbsoluteUri: true } next || next.Scheme != "https"
                    || next.Host != "release-assets.githubusercontent.com" || !next.IsDefaultPort || next.UserInfo.Length != 0)
                    throw new InvalidDataException("Update download redirected outside the official CDN.");
                uri = next;
                continue;
            }
            response.EnsureSuccessStatusCode();
            await using Stream input = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            await using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, FileOptions.Asynchronous);
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            byte[] buffer = new byte[65536];
            long size = 0;
            int read;
            while ((read = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) != 0)
            {
                size += read;
                if (size > asset.Size) throw new InvalidDataException("Update exceeds the published size.");
                hash.AppendData(buffer, 0, read);
                await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            }
            if (size != asset.Size || Convert.ToHexString(hash.GetHashAndReset()) != asset.Hash)
                throw new InvalidDataException("Update does not match the published digest.");
            return;
        }
        throw new InvalidDataException("Too many update redirects.");
    }

    private static void LaunchInstaller(string path, string hash, bool startHost)
    {
        string powershell = Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");
        var start = new ProcessStartInfo(powershell) { UseShellExecute = false, CreateNoWindow = true };
        foreach (string arg in new[] { "-NoProfile", "-NonInteractive", "-EncodedCommand", Convert.ToBase64String(Encoding.Unicode.GetBytes(InstallScript)) })
            start.ArgumentList.Add(arg);
        start.Environment["MOYAI_UPDATE_PACKAGE"] = path;
        start.Environment["MOYAI_UPDATE_HASH"] = hash;
        start.Environment["MOYAI_UPDATE_PARENT"] = Environment.ProcessId.ToString(CultureInfo.InvariantCulture);
        start.Environment["MOYAI_UPDATE_TRAY"] = Environment.ProcessPath!;
        start.Environment["MOYAI_UPDATE_AUTOSTART"] = startHost ? "1" : "0";
        start.Environment["MOYAI_UPDATE_LOG"] = Path.Combine(MoyaiSettings.DefaultLogDirectory, "startup-update.log");
        using Process process = Process.Start(start) ?? throw new IOException("Could not start the update installer.");
    }

    // The helper waits for the tray to release its binaries and runs under the original user.
    private const string InstallScript = """
        $ErrorActionPreference = 'Stop'
        $package = $env:MOYAI_UPDATE_PACKAGE
        $tray = $env:MOYAI_UPDATE_TRAY
        $status = 'failed'
        try {
            $parent = Get-Process -Id ([int]$env:MOYAI_UPDATE_PARENT) -ErrorAction SilentlyContinue
            if ($parent -and -not $parent.WaitForExit(30000)) { throw 'Tray did not exit' }
            $stream = [IO.File]::Open($package, 'Open', 'Read', 'Read')
            try {
                if ((Get-FileHash -InputStream $stream).Hash -cne $env:MOYAI_UPDATE_HASH) { throw 'Digest mismatch' }
                $signature = Get-AuthenticodeSignature -LiteralPath $package
                if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.GetNameInfo('SimpleName', $false) -cne 'Akatsukisoft') {
                    throw 'Publisher verification failed'
                }
                $arguments = '/i "' + $package + '" /passive /norestart REBOOT=ReallySuppress'
                $installer = Start-Process -FilePath ([IO.Path]::Combine([Environment]::SystemDirectory,'msiexec.exe')) -ArgumentList $arguments -Verb RunAs -Wait -PassThru
                $status = 'msi_exit_' + $installer.ExitCode
            } finally { $stream.Dispose() }
        } catch {
            $status = 'failed_' + $_.Exception.GetType().Name
        } finally {
            try {
                [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($env:MOYAI_UPDATE_LOG)) | Out-Null
                [IO.File]::AppendAllText($env:MOYAI_UPDATE_LOG, ([DateTimeOffset]::UtcNow.ToString('O') + ' ' + $status + [Environment]::NewLine))
                [IO.File]::Delete($package)
                [IO.Directory]::Delete([IO.Path]::GetDirectoryName($package))
            } finally {
                $arguments = @('--skip-update')
                if ($env:MOYAI_UPDATE_AUTOSTART -eq '1') { $arguments += '--autostart' }
                Start-Process -FilePath $tray -ArgumentList $arguments
            }
        }
        """;
}

internal sealed record UpdateAsset(Uri Url, long Size, string Hash);
