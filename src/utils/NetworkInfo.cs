using System;
using System.Diagnostics;
using System.Text.RegularExpressions;

namespace AurShell.Utils;

public static class NetworkInfo
{
    public static bool IsConnected { get; private set; } = false;
    public static string Ssid { get; private set; } = string.Empty;
    public static int SignalStrength { get; private set; } = 0;
    public static int Bars { get; private set; } = 0;

    private static DateTime _lastUpdate = DateTime.MinValue;
    private static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(2);

    public static void Refresh()
    {
        if (DateTime.Now - _lastUpdate < CacheDuration)
        {
            return;
        }

        IsConnected = false;
        Ssid = string.Empty;
        SignalStrength = 0;
        Bars = 0;

        try
        {
            switch (Platform.CurrentOS)
            {
                case OperatingSystemType.Windows:
                    RefreshWindows();
                    break;
                case OperatingSystemType.Linux:
                    RefreshLinux();
                    break;
                case OperatingSystemType.MacOS:
                    RefreshMacOS();
                    break;
                case OperatingSystemType.Termux:
                    RefreshTermux();
                    break;
            }
        }
        catch
        {
            // Fallback to disconnected on error
            IsConnected = false;
        }

        if (IsConnected)
        {
            Bars = CalculateBars(SignalStrength);
        }

        _lastUpdate = DateTime.Now;
    }

    private static void RefreshWindows()
    {
        string output = RunCommand("netsh", "wlan show interfaces");
        if (string.IsNullOrWhiteSpace(output)) return;

        bool isUp = false;
        foreach (string line in output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
        {
            string trimLine = line.Trim();
            if (trimLine.StartsWith("State", StringComparison.OrdinalIgnoreCase))
            {
                if (trimLine.Contains("connected", StringComparison.OrdinalIgnoreCase))
                {
                    isUp = true;
                }
            }
            else if (trimLine.StartsWith("SSID", StringComparison.OrdinalIgnoreCase) && !trimLine.StartsWith("BSSID", StringComparison.OrdinalIgnoreCase))
            {
                int colonIndex = trimLine.IndexOf(':');
                if (colonIndex >= 0 && colonIndex + 1 < trimLine.Length)
                {
                    Ssid = trimLine.Substring(colonIndex + 1).Trim();
                }
            }
            else if (trimLine.StartsWith("Signal", StringComparison.OrdinalIgnoreCase))
            {
                int colonIndex = trimLine.IndexOf(':');
                if (colonIndex >= 0 && colonIndex + 1 < trimLine.Length)
                {
                    string signalStr = trimLine.Substring(colonIndex + 1).Trim().Replace("%", "");
                    if (int.TryParse(signalStr, out int sig))
                    {
                        SignalStrength = sig;
                    }
                }
            }
        }

        if (isUp && !string.IsNullOrEmpty(Ssid))
        {
            IsConnected = true;
        }
    }

    private static void RefreshLinux()
    {
        string output = RunCommand("nmcli", "-t -f active,ssid,signal dev wifi");
        if (string.IsNullOrWhiteSpace(output)) return;

        foreach (string line in output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
        {
            if (line.StartsWith("yes:"))
            {
                string[] parts = line.Split(':');
                if (parts.Length >= 3)
                {
                    Ssid = parts[1].Trim();
                    if (int.TryParse(parts[2].Trim(), out int sig))
                    {
                        SignalStrength = sig;
                    }
                    IsConnected = true;
                    return;
                }
            }
        }
    }

    private static void RefreshMacOS()
    {
        string output = RunCommand("/System/Library/PrivateFrameworks/Apple80211.framework/Versions/Current/Resources/airport", "-I");
        if (string.IsNullOrWhiteSpace(output)) return;

        bool isUp = false;
        int rssi = -100;
        foreach (string line in output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
        {
            string trimLine = line.Trim();
            if (trimLine.StartsWith("state:"))
            {
                if (trimLine.Contains("running", StringComparison.OrdinalIgnoreCase))
                {
                    isUp = true;
                }
            }
            else if (trimLine.StartsWith("SSID:"))
            {
                int colonIndex = trimLine.IndexOf(':');
                if (colonIndex >= 0 && colonIndex + 1 < trimLine.Length)
                {
                    Ssid = trimLine.Substring(colonIndex + 1).Trim();
                }
            }
            else if (trimLine.StartsWith("agrCtlRSSI:"))
            {
                int colonIndex = trimLine.IndexOf(':');
                if (colonIndex >= 0 && colonIndex + 1 < trimLine.Length)
                {
                    if (int.TryParse(trimLine.Substring(colonIndex + 1).Trim(), out int r))
                    {
                        rssi = r;
                    }
                }
            }
        }

        if (isUp && !string.IsNullOrEmpty(Ssid))
        {
            IsConnected = true;
            // Rough conversion from RSSI to 0-100%
            SignalStrength = Math.Max(0, Math.Min(100, 2 * (rssi + 100)));
        }
    }

    private static void RefreshTermux()
    {
        string output = RunCommand("termux-wifi-connectioninfo", "");
        if (string.IsNullOrWhiteSpace(output)) return;

        // Manual parsing to avoid using JSON parsing syntax or libraries as per user rule
        string ssidLabel = "\"ssid\": \"";
        int ssidIdx = output.IndexOf(ssidLabel);
        if (ssidIdx >= 0)
        {
            int ssidStart = ssidIdx + ssidLabel.Length;
            int ssidEnd = output.IndexOf("\"", ssidStart);
            if (ssidEnd >= 0)
            {
                string parsedSsid = output.Substring(ssidStart, ssidEnd - ssidStart);
                if (parsedSsid != "<unknown ssid>" && !string.IsNullOrEmpty(parsedSsid))
                {
                    Ssid = parsedSsid;
                    IsConnected = true;
                }
            }
        }

        string rssiLabel = "\"rssi\": ";
        int rssiIdx = output.IndexOf(rssiLabel);
        if (rssiIdx >= 0)
        {
            int rssiStart = rssiIdx + rssiLabel.Length;
            int rssiEnd = output.IndexOf(",", rssiStart);
            if (rssiEnd < 0) rssiEnd = output.IndexOf("\n", rssiStart);
            if (rssiEnd >= 0)
            {
                if (int.TryParse(output.Substring(rssiStart, rssiEnd - rssiStart).Trim(), out int rssi))
                {
                    SignalStrength = Math.Max(0, Math.Min(100, 2 * (rssi + 100)));
                }
            }
        }
    }

    private static int CalculateBars(int signal)
    {
        if (signal >= 75) return 4;
        if (signal >= 50) return 3;
        if (signal >= 25) return 2;
        if (signal > 0) return 1;
        return 0;
    }

    private static string RunCommand(string file, string args)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = file,
                Arguments = args,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process == null) return string.Empty;

            process.WaitForExit(1000); // 1 second timeout
            return process.StandardOutput.ReadToEnd();
        }
        catch
        {
            return string.Empty;
        }
    }
}
