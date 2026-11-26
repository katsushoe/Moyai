#nullable enable
namespace Moyai.Tray;

partial class SettingsForm
{
    private TableLayoutPanel layout = null!;
    private CheckBox autoUpdate = null!;
    private Label timeoutLabel = null!;
    private NumericUpDown timeout = null!;
    private Label note = null!;
    private Label feedback = null!;
    private FlowLayoutPanel buttons = null!;
    private Button cancel = null!;
    private Button save = null!;

    private void InitializeComponent()
    {
        var resources = new System.ComponentModel.ComponentResourceManager(typeof(SettingsForm));
        layout = new TableLayoutPanel();
        autoUpdate = new CheckBox();
        timeoutLabel = new Label();
        timeout = new NumericUpDown();
        note = new Label();
        feedback = new Label();
        buttons = new FlowLayoutPanel();
        cancel = new Button();
        save = new Button();
        ((System.ComponentModel.ISupportInitialize)timeout).BeginInit();
        SuspendLayout();
        layout.ColumnCount = 2;
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 72));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 28));
        layout.RowCount = 5;
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Padding = new Padding(16);
        layout.Dock = DockStyle.Fill;
        layout.Controls.Add(autoUpdate, 0, 0);
        layout.SetColumnSpan(autoUpdate, 2);
        layout.Controls.Add(timeoutLabel, 0, 1);
        layout.Controls.Add(timeout, 1, 1);
        layout.Controls.Add(note, 0, 2);
        layout.SetColumnSpan(note, 2);
        layout.Controls.Add(feedback, 0, 3);
        layout.SetColumnSpan(feedback, 2);
        layout.Controls.Add(buttons, 0, 4);
        layout.SetColumnSpan(buttons, 2);
        autoUpdate.AutoSize = true;
        autoUpdate.TabIndex = 0;
        autoUpdate.Text = resources.GetString("AutoUpdate");
        timeoutLabel.AutoSize = true;
        timeoutLabel.Anchor = AnchorStyles.Left;
        timeoutLabel.Text = resources.GetString("Timeout");
        timeout.Minimum = 1;
        timeout.Maximum = 3600;
        timeout.Dock = DockStyle.Fill;
        timeout.TabIndex = 1;
        timeout.AccessibleName = timeoutLabel.Text;
        note.Dock = DockStyle.Fill;
        note.Text = resources.GetString("Note");
        feedback.AutoSize = true;
        feedback.ForeColor = SystemColors.HotTrack;
        buttons.AutoSize = true;
        buttons.Dock = DockStyle.Fill;
        buttons.FlowDirection = FlowDirection.RightToLeft;
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(save);
        save.AutoSize = true;
        save.Text = resources.GetString("Save");
        save.Click += Save_Click;
        cancel.AutoSize = true;
        cancel.Text = resources.GetString("Cancel");
        cancel.DialogResult = DialogResult.Cancel;
        AcceptButton = save;
        CancelButton = cancel;
        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(520, 220);
        MinimumSize = new Size(480, 250);
        StartPosition = FormStartPosition.CenterScreen;
        MaximizeBox = false;
        MinimizeBox = false;
        Text = resources.GetString("Title");
        Controls.Add(layout);
        ((System.ComponentModel.ISupportInitialize)timeout).EndInit();
        ResumeLayout(false);
    }
}
