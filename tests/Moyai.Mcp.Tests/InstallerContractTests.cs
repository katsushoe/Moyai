using System.Xml.Linq;

namespace Moyai.Mcp.Tests;

public sealed class InstallerContractTests
{
    private static readonly XNamespace Wix = "http://wixtoolset.org/schemas/v4/wxs";
    private static readonly XNamespace Util = "http://wixtoolset.org/schemas/v4/wxs/util";

    [Fact]
    public void UserHostReplacesServiceAndUsesTransactionalImpersonatedProfileSetup()
    {
        XDocument document = LoadInstaller();
        Assert.Empty(document.Descendants(Wix + "ServiceInstall"));
        Assert.Empty(document.Descendants(Wix + "ServiceControl"));
        foreach (string actionId in new[] { "InitializeUserProfile", "RemoveUserAutostart" })
        {
            XElement action = document.Descendants(Wix + "CustomAction").Single(value => (string?)value.Attribute("Id") == actionId);
            Assert.Equal("ClientSetup", (string?)action.Attribute("BinaryRef"));
            Assert.Equal("deferred", (string?)action.Attribute("Execute"));
            Assert.Equal("yes", (string?)action.Attribute("Impersonate"));
            Assert.Contains("--tray-path", (string?)action.Attribute("ExeCommand"));
        }
        foreach (string prefix in new[] { "Rollback", "Commit" })
        {
            XElement action = document.Descendants(Wix + "CustomAction").Single(value => (string?)value.Attribute("Id") == prefix + "InitializeUserProfile");
            Assert.Equal(prefix.ToLowerInvariant(), (string?)action.Attribute("Execute"));
            Assert.Equal("ClientSetup", (string?)action.Attribute("BinaryRef"));
            Assert.Equal("yes", (string?)action.Attribute("Impersonate"));
        }
        Assert.Single(document.Descendants(Wix + "Shortcut"));
    }

    [Fact]
    public void LegacyDataIsRetainedWithoutBroadUsersWriteAccess()
    {
        XDocument document = LoadInstaller();
        foreach (string directory in new[] { "DataFolder", "LogsFolder" })
        {
            XElement component = Assert.Single(document.Descendants(Wix + "Component").Where(value => (string?)value.Attribute("Directory") == directory));
            Assert.Empty(component.Descendants(Wix + "File"));
            Assert.Contains("(A;OICI;0x1301BF;;;LS)", (string?)Assert.Single(component.Descendants(Wix + "PermissionEx")).Attribute("Sddl"));
            Assert.DoesNotContain(";;;BU)", (string?)Assert.Single(component.Descendants(Wix + "PermissionEx")).Attribute("Sddl"));
        }
        Assert.Empty(document.Descendants(Wix + "RemoveFile"));
        Assert.Empty(document.Descendants(Wix + "RemoveFolder"));
        Assert.DoesNotContain(document.Descendants(Wix + "RegistryValue"), value => (string?)value.Attribute("Name") == "McpUrl");
        Assert.Equal(5, document.Descendants(Wix + "RegistrySearch").Count());
    }

    [Fact]
    public void InstallationDirectoryIsSelectablePersistedAndWrittenBeforeProfileInitialization()
    {
        XDocument document = LoadInstaller();
        XElement defaultDirectory = Assert.Single(document.Descendants(Wix + "SetDirectory"));
        Assert.Equal("NOT INSTALLFOLDER", (string?)defaultDirectory.Attribute("Condition"));
        Assert.Equal("[WindowsVolume]Moyai", (string?)defaultDirectory.Attribute("Value"));
        XElement saved = document.Descendants(Wix + "RegistryValue").Single(value => (string?)value.Attribute("Name") == "InstallDirectory");
        Assert.Equal("[INSTALLFOLDER]", (string?)saved.Attribute("Value"));
        XElement search = document.Descendants(Wix + "RegistrySearch").Single(value => (string?)value.Attribute("Name") == "InstallDirectory");
        Assert.Equal("INSTALLFOLDER", (string?)search.Parent!.Attribute("Id"));
        Assert.Equal("always64", (string?)search.Attribute("Bitness"));
        Assert.Contains(document.Descendants(Wix + "Control"), value => (string?)value.Attribute("Type") == "PathEdit" && (string?)value.Attribute("Property") == "INSTALLFOLDER");
        XElement profile = document.Descendants(Wix + "Custom").Single(value => (string?)value.Attribute("Action") == "RollbackInitializeUserProfile");
        Assert.Equal("WriteRegistryValues", (string?)profile.Attribute("After"));
        foreach (string id in new[] { "InitializeUserProfile", "ConfigureCodex", "ConfigureClaude" })
        {
            XElement action = document.Descendants(Wix + "CustomAction").Single(value => (string?)value.Attribute("Id") == id);
            Assert.Contains("--config \"[ConfigFolder]moyai.json\"", (string?)action.Attribute("ExeCommand"));
        }
    }

    [Fact]
    public void CanonicalConfigurationAccessIsBoundToPersistedInstallerUser()
    {
        XDocument document = LoadInstaller();
        XElement component = document.Descendants(Wix + "Component").Single(value => (string?)value.Attribute("Id") == "CreateConfigFolder");
        string sddl = (string)Assert.Single(component.Descendants(Wix + "PermissionEx")).Attribute("Sddl")!;
        Assert.Equal("D:PAI(A;OICI;FA;;;SY)(A;OICI;FA;;;BA)", sddl);
        Assert.DoesNotContain("[", sddl, StringComparison.Ordinal);
        Assert.DoesNotContain(";;;LS)", sddl, StringComparison.Ordinal);
        Assert.DoesNotContain(";;;BU)", sddl, StringComparison.Ordinal);
        XElement user = Assert.Single(component.Descendants(Util + "PermissionEx"));
        Assert.Equal("[MOYAI_CONFIG_OWNER_ACCOUNT]", (string?)user.Attribute("User"));
        Assert.Null(user.Attribute("Append"));
        Assert.Equal("yes", (string?)user.Attribute("Inheritable"));
        Assert.Null(user.Attribute("GenericAll"));
        Assert.Null(user.Attribute("ChangePermission"));
        Assert.Null(user.Attribute("TakeOwnership"));
        XElement owner = document.Descendants(Wix + "RegistryValue").Single(value => (string?)value.Attribute("Name") == "ConfigOwnerSid");
        Assert.Equal("[MOYAI_CONFIG_OWNER_SID]", (string?)owner.Attribute("Value"));
    }

    [Fact]
    public void OwnerResolutionPrecedesTransactionalOldProductRemoval()
    {
        XDocument document = LoadInstaller();
        Assert.Equal("afterInstallInitialize", (string?)Assert.Single(document.Descendants(Wix + "MajorUpgrade")).Attribute("Schedule"));
        XElement resolver = document.Descendants(Wix + "CustomAction").Single(value => (string?)value.Attribute("Id") == "ResolveConfigOwner");
        Assert.Equal("ConfigOwnerActions", (string?)resolver.Attribute("BinaryRef"));
        Assert.Equal("ResolveConfigurationOwner", (string?)resolver.Attribute("DllEntry"));
        Assert.Equal("immediate", (string?)resolver.Attribute("Execute"));
        XElement schedule = document.Descendants(Wix + "Custom").Single(value => (string?)value.Attribute("Action") == "ResolveConfigOwner");
        Assert.Equal("InstallInitialize", (string?)schedule.Attribute("Before"));
        Assert.Contains("NOT REMOVE", (string?)schedule.Attribute("Condition"), StringComparison.Ordinal);
    }

    [Fact]
    public void NativeSddlNeverContainsAnMsiPropertyReference()
    {
        foreach (XElement permission in LoadInstaller().Descendants(Wix + "PermissionEx"))
        {
            Assert.DoesNotContain("[", (string)permission.Attribute("Sddl")!, StringComparison.Ordinal);
            Assert.DoesNotContain("]", (string)permission.Attribute("Sddl")!, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("Codex")]
    [InlineData("Claude")]
    public void ClientActionsUseExplicitProfileImpersonationAndRollback(string client)
    {
        XDocument document = LoadInstaller();
        foreach (string operation in new[] { "Configure", "Unconfigure" })
        {
            string id = operation + client;
            XElement action = document.Descendants(Wix + "CustomAction").Single(value => (string?)value.Attribute("Id") == id);
            Assert.Equal("yes", (string?)action.Attribute("Impersonate"));
            Assert.Equal("ClientSetup", (string?)action.Attribute("BinaryRef"));
            Assert.Contains("--profile", (string?)action.Attribute("ExeCommand"));
            Assert.Contains("--transaction", (string?)action.Attribute("ExeCommand"));
            XElement rollback = document.Descendants(Wix + "CustomAction").Single(value => (string?)value.Attribute("Id") == "Rollback" + id);
            Assert.Equal("rollback", (string?)rollback.Attribute("Execute"));
            XElement sequence = document.Descendants(Wix + "Custom").Single(value => (string?)value.Attribute("Action") == id);
            Assert.Equal("Rollback" + id, (string?)sequence.Attribute("After"));
            if (operation == "Unconfigure") Assert.Contains("NOT UPGRADINGPRODUCTCODE", (string?)sequence.Attribute("Condition"));
        }
    }

    private static XDocument LoadInstaller()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Moyai.slnx"))) directory = directory.Parent;
        Assert.NotNull(directory);
        return XDocument.Load(Path.Combine(directory.FullName, "installer", "Moyai.wxs"));
    }
}
