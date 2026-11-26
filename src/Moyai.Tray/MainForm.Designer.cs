#nullable enable
namespace Moyai.Tray;

partial class MainForm
{
    private System.ComponentModel.IContainer? components;
    private NotifyIcon trayIcon = null!;
    private ContextMenuStrip trayMenu = null!;
    private ToolStripMenuItem serverItem = null!;
    private ToolStripMenuItem serverStateItem = null!;
    private ToolStripMenuItem serverCheckItem = null!;
    private ToolStripMenuItem startItem = null!;
    private ToolStripMenuItem stopItem = null!;
    private ToolStripMenuItem restartItem = null!;
    private ToolStripMenuItem configurationItem = null!;
    private ToolStripMenuItem autostartItem = null!;
    private ToolStripMenuItem statusItem = null!;
    private ToolStripMenuItem settingsItem = null!;
    private ToolStripMenuItem aboutItem = null!;
    private ToolStripMenuItem exitItem = null!;
    private TextBox statusDetails = null!;
    private Button closeStatusButton = null!;

    protected override void Dispose(bool disposing)
    {
        if (disposing) components?.Dispose();
        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        System.ComponentModel.ComponentResourceManager resources = new(typeof(MainForm));
        components = new System.ComponentModel.Container();
        trayMenu = new ContextMenuStrip(components);
        trayMenu.Renderer = new StatusMenuRenderer();
        serverItem = new ToolStripMenuItem();
        serverStateItem = new ToolStripMenuItem();
        serverCheckItem = new ToolStripMenuItem();
        startItem = new ToolStripMenuItem();
        stopItem = new ToolStripMenuItem();
        restartItem = new ToolStripMenuItem();
        configurationItem = new ToolStripMenuItem();
        autostartItem = new ToolStripMenuItem();
        statusItem = new ToolStripMenuItem();
        settingsItem = new ToolStripMenuItem();
        aboutItem = new ToolStripMenuItem();
        exitItem = new ToolStripMenuItem();
        statusDetails = new TextBox();
        closeStatusButton = new Button();
        trayIcon = new NotifyIcon(components);
        SuspendLayout();
        trayMenu.Items.AddRange(new ToolStripItem[] { restartItem, new ToolStripSeparator(), autostartItem, statusItem, settingsItem, configurationItem, aboutItem, serverItem, new ToolStripSeparator(), exitItem });
        serverItem.Name = "serverItem";
        serverItem.Text = resources.GetString("McpServerMenu") ?? "MCP server";
        serverItem.DropDownItems.AddRange(new ToolStripItem[] { serverStateItem, new ToolStripSeparator(), serverCheckItem, startItem, stopItem });
        serverItem.DropDown.Renderer = trayMenu.Renderer;
        serverItem.DropDownOpening += ServerItem_DropDownOpening;
        serverStateItem.Name = "serverStateItem";
        serverStateItem.Text = resources.GetString("ServerStatus") ?? "Status";
        serverStateItem.Enabled = false;
        serverCheckItem.Name = "serverCheckItem";
        serverCheckItem.Text = resources.GetString("ServerCheck") ?? "Check status";
        serverCheckItem.Click += ServerCheckItem_Click;
        trayMenu.Name = "trayMenu";
        trayMenu.Size = new Size(220, 140);
        startItem.Name = "startItem";
        startItem.Size = new Size(219, 22);
        startItem.Text = resources.GetString("ServerStart") ?? "Start";
        startItem.Click += StartItem_Click;
        stopItem.Name = "stopItem";
        stopItem.Size = new Size(219, 22);
        stopItem.Text = resources.GetString("ServerStop") ?? "Stop";
        stopItem.Click += StopItem_Click;
        restartItem.Name = "restartItem";
        restartItem.Text = resources.GetString("RestartHost") ?? "Restart MCP host";
        restartItem.Click += RestartItem_Click;
        configurationItem.Name = "configurationItem";
        configurationItem.Enabled = false;
        configurationItem.Text = resources.GetString("Config_unknown") ?? "Configuration status unknown; show status to refresh";
        autostartItem.CheckOnClick = false;
        autostartItem.Name = "autostartItem";
        autostartItem.Size = new Size(219, 22);
        autostartItem.Text = resources.GetString("Autostart") ?? "Start at Windows logon";
        autostartItem.Click += AutostartItem_Click;
        statusItem.Name = "statusItem";
        statusItem.Size = new Size(219, 22);
        statusItem.Text = resources.GetString("ShowStatus") ?? "Show status";
        statusItem.Click += ShowStatusItem_Click;
        settingsItem.Name = "settingsItem";
        settingsItem.Text = resources.GetString("Settings") ?? "Settings";
        settingsItem.Click += SettingsItem_Click;
        aboutItem.Name = "aboutItem";
        aboutItem.Text = resources.GetString("About") ?? "About Moyai";
        aboutItem.Click += AboutItem_Click;
        exitItem.Name = "exitItem";
        exitItem.Size = new Size(219, 22);
        exitItem.Text = resources.GetString("ExitTray") ?? "Exit tray controller";
        exitItem.Click += ExitItem_Click;
        trayIcon.ContextMenuStrip = trayMenu;
        trayIcon.Text = "Moyai MCP";
        trayIcon.Visible = true;
        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        statusDetails.Name = "statusDetails";
        statusDetails.AccessibleName = resources.GetString("HostStatusTitle");
        statusDetails.Location = new Point(16, 16);
        statusDetails.Size = new Size(528, 284);
        statusDetails.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        statusDetails.Multiline = true;
        statusDetails.ReadOnly = true;
        statusDetails.ScrollBars = ScrollBars.Vertical;
        statusDetails.TabIndex = 0;
        closeStatusButton.Name = "closeStatusButton";
        closeStatusButton.Location = new Point(448, 316);
        closeStatusButton.Size = new Size(96, 28);
        closeStatusButton.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
        closeStatusButton.Text = resources.GetString("CloseStatus") ?? "Close";
        closeStatusButton.TabIndex = 1;
        closeStatusButton.UseVisualStyleBackColor = true;
        closeStatusButton.Click += CloseStatus_Click;
        CancelButton = closeStatusButton;
        Controls.Add(statusDetails);
        Controls.Add(closeStatusButton);
        ClientSize = new Size(560, 360);
        MinimumSize = new Size(480, 260);
        StartPosition = FormStartPosition.CenterScreen;
        ShowInTaskbar = false;
        Text = resources.GetString("$this.Text") ?? "Moyai";
        Shown += MainForm_Shown;
        FormClosing += MainForm_FormClosing;
        ResumeLayout(false);
    }
}
