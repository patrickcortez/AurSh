using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace AurShell.Utils;

public static class BatteryInfo
{
    public static bool HasBattery { get; private set; }
    public static int Percent { get; private set; }
    public static bool IsCharging { get; private set; }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetSystemPowerStatus(out SYSTEM_POWER_STATUS sps);

    [StructLayout(LayoutKind.Sequential)]
    private struct SYSTEM_POWER_STATUS
    {
        public byte ACLineStatus;
        public byte BatteryFlag;
        public byte BatteryLifePercent;
        public byte SystemStatusFlag;
        public int BatteryLifeTime;
        public int BatteryFullLifeTime;
    }

    public static void Refresh()
    {
        HasBattery = false;
        Percent = 0;
        IsCharging = false;

        try
        {
            if (OperatingSystem.IsWindows())
            {
                if (GetSystemPowerStatus(out SYSTEM_POWER_STATUS sps))
                {
                    if (sps.BatteryFlag != 128 && sps.BatteryFlag != 255) // 128 = No system battery, 255 = unknown
                    {
                        HasBattery = true;
                        Percent = sps.BatteryLifePercent;
                        IsCharging = sps.ACLineStatus == 1; // 1 = Online (Charging)
                    }
                }
            }
            else if (OperatingSystem.IsLinux() || OperatingSystem.IsAndroid() || Platform.CurrentOS == OperatingSystemType.Termux)
            {
                string[] possiblePaths = {
                    "/sys/class/power_supply/BAT0",
                    "/sys/class/power_supply/BAT1",
                    "/sys/class/power_supply/battery",
                    "/sys/class/power_supply/bms"
                };

                foreach (string path in possiblePaths)
                {
                    if (Directory.Exists(path))
                    {
                        string capFile = Path.Combine(path, "capacity");
                        string statusFile = Path.Combine(path, "status");

                        if (File.Exists(capFile))
                        {
                            string capStr = File.ReadAllText(capFile).Trim();
                            if (int.TryParse(capStr, out int p))
                            {
                                HasBattery = true;
                                Percent = p > 100 ? 100 : p;

                                if (File.Exists(statusFile))
                                {
                                    string stat = File.ReadAllText(statusFile).Trim().ToLowerInvariant();
                                    IsCharging = stat == "charging" || stat == "full";
                                }
                                break;
                            }
                        }
                    }
                }
            }
            else if (OperatingSystem.IsMacOS())
            {
                var psi = new ProcessStartInfo("pmset", "-g batt")
                {
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var proc = Process.Start(psi);
                if (proc != null)
                {
                    string output = proc.StandardOutput.ReadToEnd();
                    proc.WaitForExit();

                    if (output.Contains("InternalBattery") || output.Contains("Battery"))
                    {
                        HasBattery = true;
                        IsCharging = output.Contains("charging") || output.Contains("AC Power");

                        int percentIndex = output.IndexOf("%");
                        if (percentIndex > 0)
                        {
                            int start = percentIndex - 1;
                            while (start >= 0 && char.IsDigit(output[start]))
                            {
                                start--;
                            }
                            string numStr = output.Substring(start + 1, percentIndex - start - 1);
                            if (int.TryParse(numStr, out int p))
                            {
                                Percent = p > 100 ? 100 : p;
                            }
                        }
                    }
                }
            }
        }
        catch
        {
            // Silently fail on error, prompt shouldn't crash
        }
    }
}
