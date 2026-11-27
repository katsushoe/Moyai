using System.Drawing;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Windows.Forms;
using Moyai.Tray;

namespace Moyai.Mcp.Tests;

public sealed class TrayIconTests
{
    [Theory]
    [InlineData(1, true)]
    [InlineData(0, false)]
    public void IsLightTaskbarReadsDwordValue(int value, bool expected)
    {
        Assert.Equal(expected, TrayIcons.IsLightTaskbar(value));
    }

    [Fact]
    public void IsLightTaskbarTreatsMissingValueAsDark()
    {
        Assert.False(TrayIcons.IsLightTaskbar(null));
    }

    [Fact]
    public void IsLightTaskbarTreatsNonDwordValueAsDark()
    {
        Assert.False(TrayIcons.IsLightTaskbar("1"));
    }

    [Fact]
    public void LightTaskbarUsesBlackTrayIcon()
    {
        Assert.Equal(TrayIcons.LightTaskbarResource, TrayIcons.TrayResourceFor(lightTaskbar: true));
    }

    [Fact]
    public void DarkTaskbarUsesWhiteTrayIcon()
    {
        Assert.Equal(TrayIcons.DarkTaskbarResource, TrayIcons.TrayResourceFor(lightTaskbar: false));
    }

    [Theory]
    [InlineData(TrayIcons.ApplicationResource, new[] { 16, 24, 32, 48, 64, 128, 256 })]
    [InlineData(TrayIcons.LightTaskbarResource, new[] { 16, 20, 24, 32 })]
    [InlineData(TrayIcons.DarkTaskbarResource, new[] { 16, 20, 24, 32 })]
    public void EmbeddedIconContainsExpectedSizes(string resourceName, int[] expectedSizes)
    {
        using Stream stream = typeof(TrayIcons).Assembly.GetManifestResourceStream(resourceName)!;

        int[] sizes = ReadIconDirectorySizes(stream);

        Assert.Equal(expectedSizes, sizes.Order().ToArray());
    }

    [Fact]
    public void LoadTrayReturnsSmallIconSize()
    {
        using Icon icon = TrayIcons.LoadTray(lightTaskbar: false);

        Assert.Equal(SystemInformation.SmallIconSize, icon.Size);
    }

    [Fact]
    public void MainFormUsesMoyaiIconsInsteadOfSystemIcon() => RunOnSta(() =>
    {
        using var form = new MainForm(false, _ => Task.FromResult("{}"));

        NotifyIcon trayIcon = (NotifyIcon)typeof(MainForm)
            .GetField("trayIcon", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form)!;

        Assert.NotNull(trayIcon.Icon);
        Assert.Equal(SystemInformation.SmallIconSize, trayIcon.Icon!.Size);
        Assert.NotEqual(SystemIcons.Application.Handle, trayIcon.Icon.Handle);
        Assert.Equal(SystemInformation.IconSize, form.Icon!.Size);
        trayIcon.Visible = false;
    });

    /// <summary>ICO ファイルのディレクトリから各画像の一辺の画素数を読み取ります（0 は 256 を表します）。</summary>
    private static int[] ReadIconDirectorySizes(Stream stream)
    {
        using var reader = new BinaryReader(stream);
        Assert.Equal(0, reader.ReadUInt16());
        Assert.Equal(1, reader.ReadUInt16());
        int count = reader.ReadUInt16();
        var sizes = new int[count];
        for (int index = 0; index < count; index++)
        {
            byte width = reader.ReadByte();
            byte height = reader.ReadByte();
            Assert.Equal(width, height);
            sizes[index] = width == 0 ? 256 : width;
            reader.ReadBytes(14);
        }
        return sizes;
    }

    private static void RunOnSta(Action action)
    {
        ExceptionDispatchInfo? error = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception exception) { error = ExceptionDispatchInfo.Capture(exception); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        error?.Throw();
    }
}
