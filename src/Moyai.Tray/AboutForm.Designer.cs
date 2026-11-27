#nullable enable
namespace Moyai.Tray;

partial class AboutForm
{
    private TableLayoutPanel layout = null!;
    private Label productName = null!;
    private Label description = null!;
    private Label versionLabel = null!;
    private Label versionValue = null!;
    private Label publisherLabel = null!;
    private Label publisherValue = null!;
    private Button closeButton = null!;

    private void InitializeComponent()
    {
        var resources = new System.ComponentModel.ComponentResourceManager(typeof(AboutForm));
        layout = new TableLayoutPanel();
        productName = new Label();
        description = new Label();
        versionLabel = new Label();
        versionValue = new Label();
        publisherLabel = new Label();
        publisherValue = new Label();
        closeButton = new Button();
        SuspendLayout();
        layout.SuspendLayout();
        layout.Dock = DockStyle.Fill;
        layout.Padding = new Padding(16);
        layout.ColumnCount = 2;
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowCount = 5;
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(productName, 0, 0);
        layout.SetColumnSpan(productName, 2);
        layout.Controls.Add(description, 0, 1);
        layout.SetColumnSpan(description, 2);
        layout.Controls.Add(versionLabel, 0, 2);
        layout.Controls.Add(versionValue, 1, 2);
        layout.Controls.Add(publisherLabel, 0, 3);
        layout.Controls.Add(publisherValue, 1, 3);
        layout.Controls.Add(closeButton, 1, 4);
        productName.AutoSize = true;
        productName.Name = "productName";
        productName.Margin = new Padding(3, 3, 3, 8);
        description.Dock = DockStyle.Fill;
        description.Text = resources.GetString("Description");
        versionLabel.AutoSize = true;
        versionLabel.Text = resources.GetString("Version");
        versionValue.AutoSize = true;
        versionValue.Name = "versionValue";
        publisherLabel.AutoSize = true;
        publisherLabel.Text = resources.GetString("Publisher");
        publisherValue.AutoSize = true;
        publisherValue.Name = "publisherValue";
        closeButton.Anchor = AnchorStyles.Right;
        closeButton.AutoSize = true;
        closeButton.Margin = new Padding(3, 12, 3, 3);
        closeButton.Text = resources.GetString("Close");
        closeButton.DialogResult = DialogResult.OK;
        AcceptButton = closeButton;
        CancelButton = closeButton;
        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(400, 190);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        Text = resources.GetString("Title");
        Controls.Add(layout);
        layout.ResumeLayout(false);
        layout.PerformLayout();
        ResumeLayout(false);
    }
}
