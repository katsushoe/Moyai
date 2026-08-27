#nullable enable

namespace Moyai.Presentation.Windows;

partial class ErrorDialogForm
{
    private System.ComponentModel.IContainer? components;
    private TableLayoutPanel rootLayout = null!;
    private PictureBox errorIconPictureBox = null!;
    private Label summaryLabel = null!;
    private Label detailsLabelControl = null!;
    private TextBox detailsTextBox = null!;
    private FlowLayoutPanel buttonPanel = null!;
    private Button closeButtonControl = null!;

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            components?.Dispose();
        }

        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        components = new System.ComponentModel.Container();
        rootLayout = new TableLayoutPanel();
        errorIconPictureBox = new PictureBox();
        summaryLabel = new Label();
        detailsLabelControl = new Label();
        detailsTextBox = new TextBox();
        buttonPanel = new FlowLayoutPanel();
        closeButtonControl = new Button();
        rootLayout.SuspendLayout();
        ((System.ComponentModel.ISupportInitialize)errorIconPictureBox).BeginInit();
        buttonPanel.SuspendLayout();
        SuspendLayout();
        rootLayout.ColumnCount = 2;
        rootLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 56F));
        rootLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        rootLayout.Controls.Add(errorIconPictureBox, 0, 0);
        rootLayout.Controls.Add(summaryLabel, 1, 0);
        rootLayout.Controls.Add(detailsLabelControl, 0, 1);
        rootLayout.Controls.Add(detailsTextBox, 0, 2);
        rootLayout.Controls.Add(buttonPanel, 0, 3);
        rootLayout.Dock = DockStyle.Fill;
        rootLayout.Padding = new Padding(16);
        rootLayout.RowCount = 4;
        rootLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 56F));
        rootLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28F));
        rootLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        rootLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48F));
        rootLayout.SetColumnSpan(detailsLabelControl, 2);
        rootLayout.SetColumnSpan(detailsTextBox, 2);
        rootLayout.SetColumnSpan(buttonPanel, 2);
        errorIconPictureBox.Anchor = AnchorStyles.None;
        errorIconPictureBox.Size = new Size(32, 32);
        errorIconPictureBox.SizeMode = PictureBoxSizeMode.CenterImage;
        summaryLabel.Dock = DockStyle.Fill;
        summaryLabel.TextAlign = ContentAlignment.MiddleLeft;
        detailsLabelControl.Dock = DockStyle.Fill;
        detailsLabelControl.TextAlign = ContentAlignment.BottomLeft;
        detailsTextBox.Dock = DockStyle.Fill;
        detailsTextBox.Font = new Font("Consolas", 9F);
        detailsTextBox.Multiline = true;
        detailsTextBox.ReadOnly = true;
        detailsTextBox.ScrollBars = ScrollBars.Both;
        detailsTextBox.WordWrap = false;
        buttonPanel.Controls.Add(closeButtonControl);
        buttonPanel.Dock = DockStyle.Fill;
        buttonPanel.FlowDirection = FlowDirection.RightToLeft;
        buttonPanel.Padding = new Padding(0, 8, 0, 0);
        closeButtonControl.DialogResult = DialogResult.OK;
        closeButtonControl.MinimumSize = new Size(96, 32);
        closeButtonControl.UseVisualStyleBackColor = true;
        AcceptButton = closeButtonControl;
        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        CancelButton = closeButtonControl;
        ClientSize = new Size(720, 460);
        Controls.Add(rootLayout);
        MaximizeBox = false;
        MinimizeBox = false;
        MinimumSize = new Size(560, 360);
        ShowIcon = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterScreen;
        rootLayout.ResumeLayout(false);
        rootLayout.PerformLayout();
        ((System.ComponentModel.ISupportInitialize)errorIconPictureBox).EndInit();
        buttonPanel.ResumeLayout(false);
        ResumeLayout(false);
    }
}
