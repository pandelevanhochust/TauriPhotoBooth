using System;
using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace PhotoBooth.Host;

public partial class App : Application
{
    private static readonly string LogFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "HihiBooth", "Data", "host_startup.log");
    private static System.Threading.Mutex? _singleInstanceMutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        _singleInstanceMutex = new System.Threading.Mutex(true, "HihiBooth_Host_SingleInstance_Mutex", out bool createdNew);
        if (!createdNew)
        {
            Log("[WARN] Another instance of PhotoBooth.Host is already running. Exiting.");
            Shutdown();
            return;
        }

        base.OnStartup(e);

        AppDomain.CurrentDomain.UnhandledException += (s, args) =>
        {
            Log($"[FATAL] AppDomain: {args.ExceptionObject}");
        };

        DispatcherUnhandledException += (s, args) =>
        {
            Log($"[FATAL] Dispatcher: {args.Exception}");
            args.Handled = true;
        };

        Log("[INFO] Host application started.");
    }

    public static void Log(string message)
    {
        string entry = $"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff}] {message}";
        try
        {
            string dir = Path.GetDirectoryName(LogFile)!;
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            File.AppendAllText(LogFile, entry + Environment.NewLine);
        }
        catch { }
    }
}
