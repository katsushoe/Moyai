using System.Runtime.InteropServices;

namespace Moyai.Application.Diagnostics;

/// <summary>対話可能なWindows環境で制御済みのエラーダイアログを表示します。</summary>
public static class ErrorDialog
{
    private const uint ErrorIcon = 0x00000010;
    private const uint SetForeground = 0x00010000;
    private const uint TopMost = 0x00040000;

    /// <summary>利用者が確認できる致命的エラーメッセージを表示します。</summary>
    public static void Show(string title, string message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        if (!OperatingSystem.IsWindows() || !Environment.UserInteractive) return;

        _ = MessageBox(IntPtr.Zero, message, title, ErrorIcon | SetForeground | TopMost);
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "MessageBoxW")]
    private static extern int MessageBox(IntPtr windowHandle, string text, string caption, uint type);
}
