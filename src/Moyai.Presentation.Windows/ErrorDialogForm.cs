namespace Moyai.Presentation.Windows;

/// <summary>エラー概要と技術的詳細を表示するダイアログです。</summary>
public sealed partial class ErrorDialogForm : Form
{
    /// <summary>表示内容を指定してダイアログを初期化します。</summary>
    public ErrorDialogForm(string title, string summary, string detailsLabel, string closeButton, string details)
    {
        InitializeComponent();
        Text = title;
        summaryLabel.Text = summary;
        detailsLabelControl.Text = detailsLabel;
        closeButtonControl.Text = closeButton;
        detailsTextBox.Text = details;
        errorIconPictureBox.Image = SystemIcons.Error.ToBitmap();
    }
}
