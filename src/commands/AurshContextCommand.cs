using System;
using System.IO;
using AurShell.Core;
using AurShell.Parser;
using AurShell.Utils;

namespace AurShell.Commands;

public static class AurshContextCommand
{
    private static string? FindAurshContextExecutable()
    {
        string exeName = Platform.CurrentOS == OperatingSystemType.Windows
            ? "aursh-context.exe"
            : "aursh-context";

        string baseDir = AppContext.BaseDirectory;
        string adjacent = Path.Combine(baseDir, exeName);
        if (File.Exists(adjacent))
            return adjacent;

        string? dir = baseDir;
        for (int i = 0; i < 6 && !string.IsNullOrEmpty(dir); i++)
        {
            string candidate = Path.Combine(dir, "bin", exeName);
            if (File.Exists(candidate))
                return candidate;
            dir = Directory.GetParent(dir)?.FullName;
        }

        string? onPath = Utils.Platform.FindExecutableInPath(
            Platform.CurrentOS == OperatingSystemType.Windows ? "aursh-context.exe" : "aursh-context");
        if (!string.IsNullOrEmpty(onPath))
            return onPath;

        return null;
    }

    public static int Execute(SimpleCommandNode cmd)
    {
        string? contextPath = FindAurshContextExecutable();
        if (string.IsNullOrEmpty(contextPath))
        {
            Console.Error.WriteLine("aursh: aursh-context: executable not found.");
            return 127;
        }

        var psi = new System.Diagnostics.ProcessStartInfo(contextPath)
        {
            UseShellExecute = false,
            CreateNoWindow = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var arg in cmd.Args)
            psi.ArgumentList.Add(arg);

        try
        {
            using var proc = System.Diagnostics.Process.Start(psi);
            if (proc == null) return 127;

            var outTask = System.Threading.Tasks.Task.Run(() =>
            {
                string? line;
                while ((line = proc.StandardOutput.ReadLine()) != null)
                    Console.WriteLine(line);
            });
            var errTask = System.Threading.Tasks.Task.Run(() =>
            {
                string? line;
                while ((line = proc.StandardError.ReadLine()) != null)
                    Console.Error.WriteLine(line);
            });

            proc.WaitForExit();
            try { outTask.Wait(System.TimeSpan.FromSeconds(2)); } catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"aursh error: {ex.Message}"); }
            try { errTask.Wait(System.TimeSpan.FromSeconds(2)); } catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"aursh error: {ex.Message}"); }
            return proc.ExitCode;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"aursh: aursh-context: {ex.Message}");
            return 127;
        }
    }
}
