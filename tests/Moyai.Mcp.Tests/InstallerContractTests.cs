using System.Xml.Linq;

namespace Moyai.Mcp.Tests;

public sealed class InstallerContractTests
{
    private static readonly XNamespace Wix = "http://wixtoolset.org/schemas/v4/wxs";

    [Fact]
    public void ServiceUsesDedicatedExecutableAndLeastPrivilegeAutomaticLifetime()
    {
        XDocument document = LoadInstaller();
        XElement service = Assert.Single(document.Descendants(Wix + "ServiceInstall"));
        Assert.Equal("Moyai", (string?)service.Attribute("Name"));
        Assert.Equal("auto", (string?)service.Attribute("Start"));
        Assert.Equal("ownProcess", (string?)service.Attribute("Type"));
        Assert.Equal(@"NT AUTHORITY\LocalService", (string?)service.Attribute("Account"));
        Assert.Equal("yes", (string?)service.Attribute("Vital"));
        XElement file = Assert.Single(service.Parent!.Elements(Wix + "File"));
        Assert.Equal("yes", (string?)file.Attribute("KeyPath"));
        Assert.EndsWith(@"\Moyai.Mcp.exe", (string?)file.Attribute("Source"));
        Assert.Contains(document.Descendants(Wix + "Exclude"), item => (string?)item.Attribute("Files") == (string?)file.Attribute("Source"));
        Assert.Equal("--config \"[ConfigFolder]moyai.json\"", (string?)service.Attribute("Arguments"));
        XElement initializer = Assert.Single(document.Descendants(Wix + "CustomAction"));
        Assert.Equal("CliExecutable", (string?)initializer.Attribute("FileRef"));
        Assert.Equal("deferred", (string?)initializer.Attribute("Execute"));
        XElement control = Assert.Single(document.Descendants(Wix + "ServiceControl"));
        Assert.Equal("install", (string?)control.Attribute("Start"));
        Assert.Equal("both", (string?)control.Attribute("Stop"));
        Assert.Equal("uninstall", (string?)control.Attribute("Remove"));
        Assert.Equal("yes", (string?)control.Attribute("Wait"));
    }

    [Fact]
    public void DataIsNotPackagedOrDeletedAndServiceCanWriteItsDirectories()
    {
        XDocument document = LoadInstaller();
        foreach (string directory in new[] { "DataFolder", "LogsFolder" })
        {
            XElement component = Assert.Single(document.Descendants(Wix + "Component").Where(value => (string?)value.Attribute("Directory") == directory));
            Assert.Empty(component.Descendants(Wix + "File"));
            Assert.Contains("(A;OICI;0x1301BF;;;LS)", (string?)Assert.Single(component.Descendants(Wix + "PermissionEx")).Attribute("Sddl"));
        }
        Assert.Empty(document.Descendants(Wix + "RemoveFile"));
        Assert.Empty(document.Descendants(Wix + "RemoveFolder"));
        XElement registry = Assert.Single(document.Descendants(Wix + "RegistryValue"));
        Assert.Equal("yes", (string?)registry.Parent!.Attribute("Permanent"));
        Assert.Equal("yes", (string?)registry.Parent.Attribute("NeverOverwrite"));
        Assert.Equal("McpUrl", (string?)registry.Attribute("Name"));
        Assert.Single(document.Descendants(Wix + "RegistrySearch"));
    }

    private static XDocument LoadInstaller()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Moyai.slnx"))) directory = directory.Parent;
        Assert.NotNull(directory);
        return XDocument.Load(Path.Combine(directory.FullName, "installer", "Moyai.wxs"));
    }
}
