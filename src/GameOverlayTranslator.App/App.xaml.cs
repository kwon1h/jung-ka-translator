using System.Windows;
using GameOverlayTranslator.App.Services;

namespace GameOverlayTranslator.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        DispatcherUnhandledException += (_, args) =>
        {
            AppLog.Write("Dispatcher unhandled exception", args.Exception);
            args.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            AppLog.Write("AppDomain unhandled exception", args.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            AppLog.Write("Task unobserved exception", args.Exception);
            args.SetObserved();
        };
        AppLog.Write("Application started");
        base.OnStartup(e);
    }
}
