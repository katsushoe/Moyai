namespace Moyai.Tray;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, eventArgs) => ReportError(eventArgs.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, eventArgs) => ReportError(eventArgs.ExceptionObject as Exception ?? new InvalidOperationException("Unhandled error."));
        TaskScheduler.UnobservedTaskException += (_, eventArgs) => { ReportError(eventArgs.Exception); eventArgs.SetObserved(); };
        try
        {
            TrayInstance.TryRun(() =>
            {
                ApplicationConfiguration.Initialize();
                Application.Run(new MainForm(args.Contains("--autostart", StringComparer.Ordinal), null,
                    !args.Contains("--skip-update", StringComparer.Ordinal)));
            });
        }
        catch (Exception exception) { ReportError(exception); Environment.ExitCode = 1; }
    }

    private static void ReportError(Exception exception)
    {
        LogError(exception);
        string message = System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ja"
            ? "トレイ操作でエラーが発生しました。Moyaiのログを確認してください。ホストの状態は別途確認してください。"
            : "A tray operation failed. Inspect the Moyai logs and check host status.";
        if (Environment.UserInteractive) MessageBox.Show(message, "Moyai", MessageBoxButtons.OK, MessageBoxIcon.Error);
    }

    internal static void LogError(Exception exception)
    {
        try
        {
            string directory = Moyai.Configuration.MoyaiSettings.DefaultLogDirectory;
            Directory.CreateDirectory(directory);
            // エラー文字列へ資格情報が混入する可能性があるため、境界ではmessageを伏せます。
            string details = exception.GetType().FullName + ": [message redacted]" + Environment.NewLine + exception.StackTrace;
            for (Exception? inner = exception.InnerException; inner is not null; inner = inner.InnerException)
                details += Environment.NewLine + inner.GetType().FullName + ": [message redacted]" + Environment.NewLine + inner.StackTrace;
            File.AppendAllText(Path.Combine(directory, "tray-errors.log"), DateTimeOffset.UtcNow.ToString("O", System.Globalization.CultureInfo.InvariantCulture) + Environment.NewLine + details + Environment.NewLine);
        }
        catch (Exception loggingError) when (loggingError is IOException or UnauthorizedAccessException or System.Security.SecurityException) { }
    }
}
