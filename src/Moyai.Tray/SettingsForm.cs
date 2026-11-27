using System.ComponentModel;
using System.Globalization;

namespace Moyai.Tray;

internal partial class SettingsForm : Form
{
    private readonly SettingsFile _settings;

    internal SettingsForm(SettingsFile settings)
    {
        _settings = settings;
        InitializeComponent();
        TrayIcons.ApplyApplicationIcon(this);
        autoUpdate.Checked = settings.AutoUpdate;
        timeout.Value = settings.Timeout;
    }

    private void Save_Click(object? sender, EventArgs e)
    {
        try
        {
            _settings.Save(autoUpdate.Checked, (int)timeout.Value);
            DialogResult = DialogResult.OK;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            Program.LogError(exception);
            feedback.Text = new ComponentResourceManager(typeof(SettingsForm)).GetString("SaveFailed", CultureInfo.CurrentUICulture);
        }
    }
}
