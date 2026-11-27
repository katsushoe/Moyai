namespace Moyai.Configuration;

/// <summary>MSI登録先、未インストールの配布物では実行ファイル配置から正規設定を解決します。</summary>
public static class OperationalConfigurationPath
{
    public static string Resolve(string binaryDirectory, string? installationDirectory = null)
    {
        if (installationDirectory is not null)
        {
            if (string.IsNullOrWhiteSpace(installationDirectory) || !Path.IsPathFullyQualified(installationDirectory))
                throw new InvalidOperationException("Registered Moyai installation directory must be an absolute path.");
            return Path.GetFullPath(Path.Combine(installationDirectory, "config", "moyai.json"));
        }

        var directory = new DirectoryInfo(Path.GetFullPath(binaryDirectory));
        if (directory.Name.Equals("host", StringComparison.OrdinalIgnoreCase)
            || directory.Name.Equals("tray", StringComparison.OrdinalIgnoreCase))
            directory = directory.Parent ?? throw new InvalidOperationException("Moyai binary directory has no parent.");
        string root = directory.Parent?.FullName ?? throw new InvalidOperationException("Moyai installation directory is unavailable.");
        return Path.Combine(root, "config", "moyai.json");
    }
}
