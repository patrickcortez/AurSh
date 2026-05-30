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

    // Wired connections get full bars and a different icon in the prompt
    public static bool IsWired { get; private set; } = false;

    private static DateTime _lastUpdate = DateTime.MinValue;
    private static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(2);

    public static void Refresh()
    {
        if (DateTime.Now - _lastUpdate < CacheDuration)
        {
            return;
        }

        IsConnected = false;
        IsWired = false;
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
        // Try wireless first
        string output = RunCommand("netsh", "wlan show interfaces");
        if (!string.IsNullOrWhiteSpace(output))
        {
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
                return;
            }
        }

        // No wireless found — check for wired/bridged Ethernet via ipconfig
        // This catches VMs with bridged networking and physical ethernet connections
        string ipOutput = RunCommand("ipconfig", "");
        if (string.IsNullOrWhiteSpace(ipOutput))
        {
            return;
        }

        string currentAdapter = string.Empty;
        bool foundActiveAdapter = false;

        foreach (string line in ipOutput.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
        {
            string trimLine = line.Trim();

            // Adapter header lines aren't indented and end with ':'
            if (!line.StartsWith(" ") && !line.StartsWith("\t") && trimLine.EndsWith(":"))
            {
                currentAdapter = trimLine.TrimEnd(':');
                continue;
            }

            // Look for an IPv4 address line — means this adapter is actually active
            if (trimLine.StartsWith("IPv4 Address", StringComparison.OrdinalIgnoreCase) ||
                trimLine.StartsWith("IPv4", StringComparison.OrdinalIgnoreCase))
            {
                int colonIdx = trimLine.IndexOf(':');
                if (colonIdx >= 0)
                {
                    string ipAddr = trimLine.Substring(colonIdx + 1).Trim();
                    // Skip loopback and APIPA addresses, they're not real connections
                    if (!ipAddr.StartsWith("127.") && !ipAddr.StartsWith("169.254.") && !string.IsNullOrEmpty(ipAddr))
                    {
                        if (!foundActiveAdapter && !string.IsNullOrEmpty(currentAdapter))
                        {
                            // Found an active wired/bridged adapter
                            Ssid = currentAdapter;
                            IsConnected = true;
                            IsWired = true;
                            SignalStrength = 100;
                            foundActiveAdapter = true;
                        }
                    }
                }
            }
        }
    }

    private static void RefreshLinux()
    {
        // Try nmcli wifi first — works on most desktop Linux with NetworkManager
        if (TryRefreshLinuxNmcliWifi())
        {
            return;
        }

        // nmcli didn't find wireless — maybe we're in a VM or wired-only box
        // Check for any active network connection via nmcli general
        if (TryRefreshLinuxNmcliGeneral())
        {
            return;
        }

        // nmcli isn't available at all — fall back to sysfs
        // This works everywhere, even minimal containers and VMs without NetworkManager
        TryRefreshLinuxSysfs();
    }

    private static bool TryRefreshLinuxNmcliWifi()
    {
        string output = RunCommand("nmcli", "-t -f active,ssid,signal dev wifi");
        if (string.IsNullOrWhiteSpace(output))
        {
            return false;
        }

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
                    return true;
                }
            }
        }

        return false;
    }

    private static bool TryRefreshLinuxNmcliGeneral()
    {
        // nmcli -t -f type,state,connection dev — shows ALL devices, not just wifi
        // Output looks like: ethernet:connected:Wired connection 1
        string output = RunCommand("nmcli", "-t -f type,state,connection dev");
        if (string.IsNullOrWhiteSpace(output))
        {
            return false;
        }

        foreach (string line in output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
        {
            string[] parts = line.Split(':');
            if (parts.Length >= 3)
            {
                string devType = parts[0].Trim();
                string devState = parts[1].Trim();
                string connName = parts[2].Trim();

                // We already tried wifi above, so focus on ethernet/bridge/veth
                if (devState.Equals("connected", StringComparison.OrdinalIgnoreCase) &&
                    !devType.Equals("wifi", StringComparison.OrdinalIgnoreCase) &&
                    !devType.Equals("loopback", StringComparison.OrdinalIgnoreCase) &&
                    !string.IsNullOrEmpty(connName))
                {
                    Ssid = connName;
                    IsConnected = true;
                    IsWired = true;
                    SignalStrength = 100;
                    return true;
                }
            }
        }

        return false;
    }

    private static bool TryRefreshLinuxSysfs()
    {
        // Read /sys/class/net/ directly — the most portable Linux approach
        // Every network interface shows up here, even in containers and VMs
        try
        {
            string netClassPath = "/sys/class/net";
            if (!Directory.Exists(netClassPath))
            {
                return false;
            }

            foreach (string interfaceDir in Directory.GetDirectories(netClassPath))
            {
                string ifName = Path.GetFileName(interfaceDir);

                // Skip loopback, it's always "up" but doesn't count as connected
                if (ifName == "lo")
                {
                    continue;
                }

                string operstatePath = Path.Combine(interfaceDir, "operstate");
                if (!File.Exists(operstatePath))
                {
                    continue;
                }

                string operstate = File.ReadAllText(operstatePath).Trim();
                if (operstate.Equals("up", StringComparison.OrdinalIgnoreCase))
                {
                    // Check if this is a wireless or wired interface
                    // Wireless interfaces have a "wireless" subdirectory in sysfs
                    bool isWireless = Directory.Exists(Path.Combine(interfaceDir, "wireless"));

                    if (isWireless)
                    {
                        // It's wireless but nmcli failed — try reading /proc/net/wireless
                        Ssid = ifName;
                        IsConnected = true;
                        IsWired = false;
                        SignalStrength = ReadWirelessSignalFromProc(ifName);
                        return true;
                    }
                    else
                    {
                        // Wired or virtual — this is what we're here for in VMs
                        Ssid = ifName;
                        IsConnected = true;
                        IsWired = true;
                        SignalStrength = 100;
                        return true;
                    }
                }
            }
        }
        catch
        {
            // sysfs might not be available in some weird container setups
        }

        return false;
    }

    private static int ReadWirelessSignalFromProc(string interfaceName)
    {
        // /proc/net/wireless gives signal level for wireless interfaces
        // Format: "Inter-| sta-|   Quality        |   Discarded packets..."
        //         " wlan0: 0000   50.  -60.  -256  ..."
        try
        {
            string procWirelessPath = "/proc/net/wireless";
            if (!File.Exists(procWirelessPath))
            {
                return 50;
            }

            foreach (string line in File.ReadAllLines(procWirelessPath))
            {
                string trimmed = line.Trim();
                if (trimmed.StartsWith(interfaceName + ":", StringComparison.Ordinal))
                {
                    // Split by whitespace after the interface name
                    string afterName = trimmed.Substring(trimmed.IndexOf(':') + 1).Trim();
                    string[] fields = afterName.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

                    // fields[1] is the link quality, fields[2] is the signal level (dBm)
                    if (fields.Length >= 3)
                    {
                        string signalField = fields[2].TrimEnd('.');
                        if (int.TryParse(signalField, out int dbm))
                        {
                            // Convert dBm to percentage (rough approximation)
                            return Math.Max(0, Math.Min(100, 2 * (dbm + 100)));
                        }
                    }
                }
            }
        }
        catch
        {
            // /proc might not be mounted or readable
        }

        return 50;
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
