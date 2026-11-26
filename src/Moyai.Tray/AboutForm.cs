using System.Reflection;

namespace Moyai.Tray;

internal partial class AboutForm : Form
{
    internal AboutForm()
    {
        InitializeComponent();
        TrayIcons.ApplyApplicationIcon(this);

        Assembly assembly = typeof(AboutForm).Assembly;
        productName.Text = assembly.GetCustomAttribute<AssemblyProductAttribute>()?.Product ?? "Moyai";
        versionValue.Text = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0]
            ?? assembly.GetName().Version?.ToString();
        publisherValue.Text = assembly.GetCustomAttribute<AssemblyCompanyAttribute>()?.Company;
    }
}
