using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace PhotoBooth.Watchdog;

public class Program
{
    private const string ProcessName = "PhotoBooth.Host";
    private static readonly TimeSpan HeartbeatTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan StartupGracePeriod = TimeSpan.FromSeconds(25);
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(3);

    public static async Task Main(string[] args)
    {
        Console.Title = "HihiBooth Kiosk Watchdog Supervisor";
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("=================================================");
        Console.WriteLine("     HIHIBOOTH KIOSK WATCHDOG SUPERVISOR         ");
        Console.WriteLine("=================================================");
        Console.ResetColor();

        string dataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "HihiBooth", "Data");
        if (!Directory.Exists(dataDir)) Directory.CreateDirectory(dataDir);
        string heartbeatFile = Path.Combine(dataDir, "heartbeat.txt");
        string logFile = Path.Combine(dataDir, "watchdog.log");

        // Refresh heartbeat timestamp on supervisor start so it doesn't judge from previous runs
        try { File.WriteAllText(heartbeatFile, DateTime.UtcNow.ToString("o")); } catch { }

        Log(logFile, "Watchdog supervisor started.");

        string hostExePath = FindHostExecutable();
        Log(logFile, $"Target Host executable: {hostExePath}");

        while (true)
        {
            try
            {
                var processes = Process.GetProcessesByName(ProcessName);
                if (processes.Length == 0)
                {
                    Log(logFile, $"[ALERT] {ProcessName} is NOT running. Attempting auto-restart...");
                    LaunchHost(hostExePath, logFile);
                    await Task.Delay(5000); // Allow startup time
                }
                else
                {
                    // Check heartbeat freshness only if at least one process has run past grace period
                    bool pastGracePeriod = false;
                    foreach (var p in processes)
                    {
                        try
                        {
                            if (DateTime.UtcNow - p.StartTime.ToUniversalTime() > StartupGracePeriod)
                            {
                                pastGracePeriod = true;
                                break;
                            }
                        }
                        catch { }
                    }

                    if (pastGracePeriod && File.Exists(heartbeatFile))
                    {
                        var lastWrite = File.GetLastWriteTimeUtc(heartbeatFile);
                        if (DateTime.UtcNow - lastWrite > HeartbeatTimeout)
                        {
                            Log(logFile, $"[ALERT] Heartbeat expired! Process unresponsive for {(DateTime.UtcNow - lastWrite).TotalSeconds:F0}s. Killing frozen instances...");
                            foreach (var p in processes)
                            {
                                try { p.Kill(true); } catch { }
                            }
                            await Task.Delay(1500);
                            LaunchHost(hostExePath, logFile);
                            await Task.Delay(5000);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log(logFile, $"[ERROR] Watchdog monitoring loop error: {ex.Message}");
            }

            await Task.Delay(PollInterval);
        }
    }

    private static string FindHostExecutable()
    {
        string[] candidates = new[]
        {
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "PhotoBooth.Host.exe"),
            Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "PhotoBooth.Host", "PhotoBooth.Host.exe")),
            Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "PhotoBooth.Host", "bin", "Debug", "net10.0-windows", "PhotoBooth.Host.exe")),
            Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "PhotoBooth.Host", "bin", "Debug", "net10.0-windows", "PhotoBooth.Host.exe")),
            @"D:\WORK\TauriPhotoBooth\backend\src\PhotoBooth.Host\bin\Debug\net10.0-windows\PhotoBooth.Host.exe"
        };

        foreach (var c in candidates)
        {
            if (File.Exists(c)) return c;
        }

        return candidates[candidates.Length - 1];
    }

    private static void LaunchHost(string exePath, string logFile)
    {
        if (File.Exists(exePath))
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = exePath,
                    UseShellExecute = true,
                    WorkingDirectory = Path.GetDirectoryName(exePath)
                };
                Process.Start(psi);
                Log(logFile, $"[INFO] Successfully launched: {exePath}");
            }
            catch (Exception ex)
            {
                Log(logFile, $"[ERROR] Failed to launch host process: {ex.Message}");
            }
        }
        else
        {
            Log(logFile, $"[WARN] Host executable not found at '{exePath}'. Waiting for manual start.");
        }
    }

    private static void Log(string logFile, string message)
    {
        string entry = $"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}] {message}";
        Console.WriteLine(entry);
        try
        {
            File.AppendAllText(logFile, entry + Environment.NewLine);
        }
        catch { }
    }
}
