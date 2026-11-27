using Microsoft.Win32;

namespace Moyai.Tray;

/// <summary>Moyaiのアプリアイコンとタスクトレイアイコンを埋め込みリソースから読み込みます。</summary>
internal static class TrayIcons
{
    internal const string ApplicationResource = "Moyai.Tray.Assets.moyai.ico";
    internal const string LightTaskbarResource = "Moyai.Tray.Assets.moyai_tray.ico";
    internal const string DarkTaskbarResource = "Moyai.Tray.Assets.moyai_tray_white.ico";

    private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private const string SystemUsesLightTheme = "SystemUsesLightTheme";

    /// <summary>ウィンドウのタイトルバー等に使うアプリアイコンを返します。</summary>
    internal static Icon LoadApplication() => Load(ApplicationResource, SystemInformation.IconSize);

    /// <summary>フォームへアプリアイコンを設定し、フォームの破棄時にアイコンも破棄します。</summary>
    internal static void ApplyApplicationIcon(Form form)
    {
        Icon icon = LoadApplication();
        form.Icon = icon;
        form.Disposed += (_, _) => icon.Dispose();
    }

    /// <summary>タスクバーの配色に合う通知領域アイコンを返します。</summary>
    internal static Icon LoadTray(bool lightTaskbar) =>
        Load(TrayResourceFor(lightTaskbar), SystemInformation.SmallIconSize);

    /// <summary>ライトのタスクバーには黒線、ダークのタスクバーには白線のアイコンを選びます。</summary>
    internal static string TrayResourceFor(bool lightTaskbar) =>
        lightTaskbar ? LightTaskbarResource : DarkTaskbarResource;

    /// <summary>現在のユーザーのタスクバーがライト配色かを返します。</summary>
    internal static bool IsLightTaskbar()
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
            return IsLightTaskbar(key?.GetValue(SystemUsesLightTheme));
        }
        catch (Exception exception) when (exception is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return false;
        }
    }

    /// <summary>
    /// SystemUsesLightTheme の値を解釈します。値が無い、または DWORD 以外の場合は
    /// Windows の既定であるダーク配色のタスクバーとして扱います。
    /// </summary>
    internal static bool IsLightTaskbar(object? systemUsesLightTheme) => systemUsesLightTheme is int value && value != 0;

    private static Icon Load(string resourceName, Size size)
    {
        using Stream stream = typeof(TrayIcons).Assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException("The embedded icon resource is missing: " + resourceName);
        return new Icon(stream, size);
    }
}
