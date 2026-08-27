namespace Moyai.Application.Diagnostics;

/// <summary>プロセス境界で捕捉されなかった例外を共通の報告先へ送ります。</summary>
public static class GlobalExceptionHandler
{
    /// <summary>未処理例外と未監視Task例外のハンドラを登録します。</summary>
    public static void Register(Action<Exception> report)
    {
        ArgumentNullException.ThrowIfNull(report);

        AppDomain.CurrentDomain.UnhandledException += (_, eventArgs) =>
            report(eventArgs.ExceptionObject as Exception ?? new InvalidOperationException("A non-Exception object reached the global exception handler."));
        TaskScheduler.UnobservedTaskException += (_, eventArgs) =>
        {
            report(eventArgs.Exception);
            eventArgs.SetObserved();
        };
    }
}
