using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace AurShell.ISE.Services;

public class AurshRunner
{
    private readonly string _aurshPath;

    public AurshRunner()
    {
        string basePath = AppDomain.CurrentDomain.BaseDirectory;
        string exeName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "aursh.exe" : "aursh";
        
        // Attempt to find the built aursh executable in the ../../../../../bin folder (typical debug structure)
        _aurshPath = Path.GetFullPath(Path.Combine(basePath, "..", "..", "..", "..", "..", "bin", exeName));
        
        if (!File.Exists(_aurshPath))
        {
            // Fallback to system PATH if not found relative to output directory
            _aurshPath = exeName;
        }
    }

    public async Task<string> RunScriptAsync(string scriptContent)
    {
        string tempFile = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(tempFile, scriptContent);

            var startInfo = new ProcessStartInfo
            {
                FileName = _aurshPath,
                Arguments = $"\"{tempFile}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = new Process { StartInfo = startInfo };
            process.Start();

            string output = await process.StandardOutput.ReadToEndAsync();
            string error = await process.StandardError.ReadToEndAsync();

            await process.WaitForExitAsync();

            string result = "";
            if (!string.IsNullOrEmpty(output)) result += output + "\n";
            if (!string.IsNullOrEmpty(error)) result += "[ERROR]\n" + error + "\n";
            
            result += $"\nProcess exited with code {process.ExitCode}";
            return result;
        }
        catch (Exception ex)
        {
            return $"[EXECUTION EXCEPTION]\n{ex.Message}\nCheck if '{_aurshPath}' exists and is executable.";
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                try { File.Delete(tempFile); } catch { }
            }
        }
    }
}
