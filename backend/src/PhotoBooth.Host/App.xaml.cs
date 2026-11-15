using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;

namespace PhotoBooth.Host;

public partial class App : Application
{
    private static readonly string LogFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "HihiBooth", "Data", "host_startup.log");
    private static System.Threading.Mutex? _singleInstanceMutex;

    // Win32 Console P/Invoke
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AttachConsole(int dwProcessId);

    private const int ATTACH_PARENT_PROCESS = -1;

    [DllImport("kernel32.dll")]
    private static extern bool SetConsoleCtrlHandler(ConsoleCtrlDelegate handler, bool add);

    private delegate bool ConsoleCtrlDelegate(int ctrlType);
    private static ConsoleCtrlDelegate? _consoleCtrlHandler;

    protected override void OnStartup(StartupEventArgs e)
    {
        _singleInstanceMutex = new System.Threading.Mutex(true, "HihiBooth_Host_SingleInstance_Mutex", out bool createdNew);
        if (!createdNew)
        {
            Log("[WARN] Another instance of PhotoBooth.Host is already running. Exiting.");
            Shutdown();
            return;
        }

        // Attach to calling terminal console so Ctrl+C cancels dotnet run properly
        try
        {
            if (AttachConsole(ATTACH_PARENT_PROCESS))
            {
                _consoleCtrlHandler = HandleConsoleCtrl;
                SetConsoleCtrlHandler(_consoleCtrlHandler, true);
                Console.CancelKeyPress += (sender, cancelArgs) =>
                {
                    cancelArgs.Cancel = true;
                    Log("[INFO] Console.CancelKeyPress received (Ctrl+C). Terminating host.");
                    ShutdownHost();
                };
            }
        }
        catch (Exception ex)
        {
            Log($"[WARN] Console hook registration error: {ex.Message}");
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

    private static bool HandleConsoleCtrl(int ctrlType)
    {
        // 0 = CTRL_C_EVENT, 1 = CTRL_BREAK_EVENT, 2 = CTRL_CLOSE_EVENT
        Log($"[INFO] Console Ctrl event received ({ctrlType}). Terminating host.");
        ShutdownHost();
        return true;
    }

    public static void ShutdownHost()
    {
        try
        {
            _singleInstanceMutex?.ReleaseMutex();
            _singleInstanceMutex?.Dispose();
            _singleInstanceMutex = null;
        }
        catch { }

        try
        {
            Current?.Dispatcher?.Invoke(() => Current?.Shutdown());
        }
        catch { }

        Environment.Exit(0);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            _singleInstanceMutex?.ReleaseMutex();
            _singleInstanceMutex?.Dispose();
            _singleInstanceMutex = null;
        }
        catch { }

        base.OnExit(e);
    }

    private static readonly object LogLock = new();
    private static int _logWrites;
    /// <summary>Log vượt cỡ này thì đổi tên thành host_startup.1.log (giữ một bản cũ, bộ dọn file xoá sau 7 ngày).</summary>
    private const long MaxLogBytes = 20L * 1024 * 1024;

    public static void Log(string message)
    {
        string entry = $"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff}] {message}";
        try
        {
            lock (LogLock)
            {
                string dir = Path.GetDirectoryName(LogFile)!;
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                if (_logWrites++ % 200 == 0) RotateLogIfLarge();
                File.AppendAllText(LogFile, entry + Environment.NewLine);
            }
        }
        catch { }
    }

    private static void RotateLogIfLarge()
    {
        try
        {
            var info = new FileInfo(LogFile);
            if (!info.Exists || info.Length < MaxLogBytes) return;
            string backup = Path.ChangeExtension(LogFile, ".1.log");
            File.Move(LogFile, backup, overwrite: true);
        }
        catch { }
    }
}
