using System.Globalization;
using System.Reflection;
using System.Resources;

namespace Moyai.Presentation.Windows;

/// <summary>予期しないエラーを利用者向けダイアログで表示します。</summary>
public static class ErrorDialog
{
    private static readonly ResourceManager Resources = new("Moyai.Presentation.Windows.ErrorDialogResources", Assembly.GetExecutingAssembly());

    /// <summary>現在のUIカルチャに合わせたエラーダイアログを表示します。</summary>
    public static void Show(string productName, Exception exception)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(productName);
        ArgumentNullException.ThrowIfNull(exception);
        if (!Environment.UserInteractive) return;

        CultureInfo culture = CultureInfo.CurrentUICulture;
        string title = string.Format(culture, GetString("DialogTitle", culture), productName);
        using var form = new ErrorDialogForm(
            title,
            GetString("Summary", culture),
            GetString("DetailsLabel", culture),
            GetString("CloseButton", culture),
            exception.ToString());
        _ = form.ShowDialog();
    }

    private static string GetString(string name, CultureInfo culture) =>
        Resources.GetString(name, culture) ?? throw new MissingManifestResourceException($"Resource '{name}' was not found.");
}
