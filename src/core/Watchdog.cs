using System;
using System.IO;
using System.Threading.Tasks;
using AurShell.Utils;

namespace AurShell.Core;

public static class Watchdog
{
    private static bool _isStarted = false;
    private static readonly object _lock = new object();

    public static void Start()
    {
        lock (_lock)
        {
            if (_isStarted) return;

            AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
            TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

            _isStarted = true;
        }
    }

    private static void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
        {
            LogCrash(ex, "AppDomain.UnhandledException");
        }
        else
        {
            LogCrash(new Exception("Unknown exception object: " + e.ExceptionObject?.ToString()), "AppDomain.UnhandledException");
        }

        if (e.IsTerminating)
        {
            Environment.Exit(1);
        }
    }

    private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        LogCrash(e.Exception, "TaskScheduler.UnobservedTaskException");
        e.SetObserved();
    }

    private static void LogCrash(Exception ex, string source)
    {
        try
        {
            string crashLogPath = Path.Combine(Platform.DataDirectory, "crash.log");

            using (StreamWriter writer = new StreamWriter(crashLogPath, append: true))
            {
                writer.WriteLine("==================================================");
                writer.WriteLine($"CRASH REPORT - {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                writer.WriteLine("==================================================");
                writer.WriteLine($"Source:       {source}");
                writer.WriteLine($"OS:           {Platform.OsName} ({Platform.CurrentOS})");
                writer.WriteLine($"Version:      2.0.0");
                writer.WriteLine($"Exception:    {ex.GetType().FullName}");
                writer.WriteLine($"Message:      {ex.Message}");
                writer.WriteLine("--------------------------------------------------");
                writer.WriteLine("Stack Trace:");
                writer.WriteLine(ex.StackTrace);
                if (ex.InnerException != null)
                {
                    writer.WriteLine("--------------------------------------------------");
                    writer.WriteLine("Inner Exception:");
                    writer.WriteLine(ex.InnerException.GetType().FullName + ": " + ex.InnerException.Message);
                    writer.WriteLine(ex.InnerException.StackTrace);
                }
                writer.WriteLine("==================================================\n");
            }

            // Output a graceful message to the user before termination
            Console.WriteLine();
            Console.WriteLine(Ansi.FgRed + "[AurSh Watchdog] A critical background error occurred." + Ansi.Reset);
            Console.WriteLine(Ansi.FgBrightYellow + $"Details have been logged to: {crashLogPath}" + Ansi.Reset);
        }
        catch
        {
            // If the crash logger itself crashes, we can't do much. 
            // Just swallow it to prevent recursive crash loops.
        }
    }
}
