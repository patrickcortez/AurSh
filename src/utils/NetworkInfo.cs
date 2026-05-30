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
    public static int LinkSpeed { get; private set; } = 0;

    // Okay, so VMs and bridged networks exist and they pretend to be wired connections.
    // It's super annoying, but we have to track them separately so the prompt doesn't look completely stupid
    // by showing a Wi-Fi icon for an ethernet cable.
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
        LinkSpeed = 0;

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

        // Final universal fallback: If we still think we are disconnected, try a quick ping.
        // This handles weird VMs, minimal Docker containers, and PRoot instances where
        // all network management tools are missing, but raw internet access is routed.
        if (!IsConnected)
        {
            if (TryPingFallback())
            {
                IsConnected = true;
                IsWired = true; // Assume wired/bridged since we have no WiFi data
                SignalStrength = 100;
                Ssid = "Connected (Ping)";
                LinkSpeed = 0;
            }
        }

        if (IsConnected)
        {
            Bars = CalculateBars(SignalStrength);
        }

        _lastUpdate = DateTime.Now;
    }

    private static bool TryPingFallback()
    {
        try
        {
            // C#'s built-in Ping is usually the fastest, but it can throw PlatformNotSupportedException
            // on Android/Termux due to raw socket restrictions for unprivileged apps.
            try 
            {
                using var ping = new System.Net.NetworkInformation.Ping();
                // 300ms timeout to prevent blocking the prompt for too long if completely offline
                var reply = ping.Send("8.8.8.8", 300);
                if (reply.Status == System.Net.NetworkInformation.IPStatus.Success)
                {
                    return true;
                }
            }
            catch
            {
                // Fallback to shell ping if C# Ping fails (common on Android PRoot)
            }

            if (Platform.CurrentOS != OperatingSystemType.Windows)
            {
                // -c 1 (1 packet), -W 1 (1 second timeout on Linux/Termux), MacOS uses -t 1 for timeout
                string args = Platform.CurrentOS == OperatingSystemType.MacOS ? "-c 1 -t 1 8.8.8.8" : "-c 1 -W 1 8.8.8.8";
                string output = RunCommand("ping", args);
                return output.Contains("1 received") || output.Contains("1 packets received");
            }

            return false;
        }
        catch
        {
            return false;
        }
    }

    private static void RefreshWindows()
    {
        // Let's pray to the Microsoft gods that this machine actually has a Wi-Fi card.
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
                else if (trimLine.StartsWith("Receive rate", StringComparison.OrdinalIgnoreCase))
                {
                    int colonIndex = trimLine.IndexOf(':');
                    if (colonIndex >= 0 && colonIndex + 1 < trimLine.Length)
                    {
                        string speedStr = trimLine.Substring(colonIndex + 1).Trim();
                        if (double.TryParse(speedStr, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double speed))
                        {
                            LinkSpeed = (int)speed;
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

        // Okay, the Wi-Fi scan completely failed. Either this poor user is on a desktop with an actual
        // ethernet cable, or they are trapped in a Virtual Machine with a bridged network.
        // It honestly drives me crazy how Windows hides this stuff, but ipconfig never lies to us.
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

            // Oh boy, parsing command line output. Windows doesn't indent headers, they just throw a colon at the end.
            if (!line.StartsWith(" ") && !line.StartsWith("\t") && trimLine.EndsWith(":"))
            {
                currentAdapter = trimLine.TrimEnd(':');
                continue;
            }

            // If we see an IPv4 address, hallelujah! The adapter is actually plugged into something and alive.
            if (trimLine.StartsWith("IPv4 Address", StringComparison.OrdinalIgnoreCase) ||
                trimLine.StartsWith("IPv4", StringComparison.OrdinalIgnoreCase))
            {
                int colonIdx = trimLine.IndexOf(':');
                if (colonIdx >= 0)
                {
                    string ipAddr = trimLine.Substring(colonIdx + 1).Trim();
                    // I refuse to count loopbacks or those weird 169.254 APIPA addresses.
                    // If you don't have a real IP, don't talk to me.
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
        // NetworkManager is pretty much the standard these days, so let's try the happy path first.
        // It makes life so much easier when it just works.
        if (TryRefreshLinuxNmcliWifi())
        {
            return;
        }

        // Okay, nmcli gave us nothing. We are probably stuck inside a VM or some server without a Wi-Fi card.
        // Let's ask nmcli about generic connections before we panic.
        if (TryRefreshLinuxNmcliGeneral())
        {
            return;
        }

        // Total nightmare scenario. nmcli is dead or missing.
        // Time to roll up our sleeves and dig through the murky depths of sysfs.
        // It's ugly, but it's the only way to survive in minimal containers.
        TryRefreshLinuxSysfs();
    }

    private static bool TryRefreshLinuxNmcliWifi()
    {
        string output = RunCommand("nmcli", "-t -f active,ssid,signal,rate dev wifi");
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
                        IsConnected = true;
                    }
                }
                if (parts.Length >= 4)
                {
                    string rateStr = parts[3].Replace("Mbit/s", "").Trim();
                    if (int.TryParse(rateStr, out int rate))
                    {
                        LinkSpeed = rate;
                    }
                }
                return true;
            }
        }

        return false;
    }

    private static bool TryRefreshLinuxNmcliGeneral()
    {
        // I literally just want to know if there's an ethernet cable plugged in,
        // but nmcli insists on dumping every device on the planet, so we have to filter it out.
        // The output usually looks like this mess: ethernet:connected:Wired connection 1
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

                // I don't care about wifi right now (we already tried it and failed),
                // and loopback is just the system talking to itself. We want the real juice!
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
        // Welcome to the absolute bottom of the barrel. We are reading raw kernel files now.
        // It feels so dirty but honestly, it's the most reliable thing on Linux.
        // If there's an interface, it's living in /sys/class/net/.
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

                // Skip the loopback interface! It always says it's "up" and gets my hopes up for nothing.
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
                    // Okay, it's up. But is it Wi-Fi or wired?
                    // You can literally just check if it has a "wireless" folder. The kernel is crazy sometimes.
                    bool isWireless = Directory.Exists(Path.Combine(interfaceDir, "wireless"));

                    if (isWireless)
                    {
                        // Ugh, it IS wireless, but nmcli let us down. I have to read the signal from /proc/net/wireless now.
                        Ssid = ifName;
                        IsConnected = true;
                        IsWired = false;
                        SignalStrength = ReadWirelessSignalFromProc(ifName);
                        return true;
                    }
                    else
                    {
                        // YES! It's a wired connection! Or a VM bridge, who cares, it has internet!
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
            // You have got to be kidding me. sysfs isn't even here? We are definitely trapped in a docker container.
            // Just give up.
        }

        return false;
    }

    private static int ReadWirelessSignalFromProc(string interfaceName)
    {
        // Don't even get me started on this file. 
        // /proc/net/wireless is so archaic, the header literally has misaligned columns.
        // It looks something like: "Inter-| sta-|   Quality        |   Discarded packets..."
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
                    // Oh joy, let's split by spaces because fixed-width parsing is too much to ask for.
                    string afterName = trimmed.Substring(trimmed.IndexOf(':') + 1).Trim();
                    string[] fields = afterName.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

                    // Field 2 is the actual signal in dBm. It has a weird dot at the end sometimes. Who designed this?!
                    if (fields.Length >= 3)
                    {
                        string signalField = fields[2].TrimEnd('.');
                        if (int.TryParse(signalField, out int dbm))
                        {
                            // Convert the negative dBm to a percentage. I'm just eyeballing the math here, but it works fine.
                            return Math.Max(0, Math.Min(100, 2 * (dbm + 100)));
                        }
                    }
                }
            }
        }
        catch
        {
            // I'm crying inside. /proc isn't even mounted. 
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
            else if (trimLine.StartsWith("lastTxRate:"))
            {
                int colonIndex = trimLine.IndexOf(':');
                if (colonIndex >= 0 && colonIndex + 1 < trimLine.Length)
                {
                    if (int.TryParse(trimLine.Substring(colonIndex + 1).Trim(), out int rate))
                    {
                        LinkSpeed = rate;
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
        if (!string.IsNullOrWhiteSpace(output))
        {
            // Parse SSID
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

            // Parse RSSI
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

            // Parse Link Speed
            string speedLabel = "\"link_speed_mbps\": ";
            int speedIdx = output.IndexOf(speedLabel);
            if (speedIdx >= 0)
            {
                int speedStart = speedIdx + speedLabel.Length;
                int speedEnd = output.IndexOf(",", speedStart);
                if (speedEnd < 0) speedEnd = output.IndexOf("\n", speedStart);
                if (speedEnd >= 0)
                {
                    if (int.TryParse(output.Substring(speedStart, speedEnd - speedStart).Trim(), out int speed))
                    {
                        LinkSpeed = speed;
                    }
                }
            }

            if (IsConnected)
            {
                return;
            }
        }

        // Fallback 1: dumpsys wifi (requires root or adb)
        string dumpsysOutput = RunCommand("su", "-c \"dumpsys wifi\"");
        if (!string.IsNullOrWhiteSpace(dumpsysOutput))
        {
            foreach (string line in dumpsysOutput.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string trimLine = line.Trim();
                if (trimLine.StartsWith("SSID: ", StringComparison.OrdinalIgnoreCase))
                {
                    string parsedSsid = trimLine.Substring(6).Trim().Trim('"');
                    if (parsedSsid != "<unknown ssid>" && !string.IsNullOrEmpty(parsedSsid))
                    {
                        Ssid = parsedSsid;
                        IsConnected = true;
                    }
                }
                else if (trimLine.StartsWith("RSSI: ", StringComparison.OrdinalIgnoreCase))
                {
                    if (int.TryParse(trimLine.Substring(6).Trim(), out int rssi))
                    {
                        SignalStrength = Math.Max(0, Math.Min(100, 2 * (rssi + 100)));
                    }
                }
                else if (trimLine.StartsWith("Link speed: ", StringComparison.OrdinalIgnoreCase))
                {
                    string speedStr = trimLine.Substring(12).Replace("Mbps", "").Trim();
                    if (int.TryParse(speedStr, out int speed))
                    {
                        LinkSpeed = speed;
                    }
                }
            }

            if (IsConnected)
            {
                return;
            }
        }

        // Fallback 2: ip addr show wlan0 (just to know we have an IP)
        string ipOutput = RunCommand("ip", "addr show wlan0");
        if (!string.IsNullOrWhiteSpace(ipOutput) && ipOutput.Contains("inet "))
        {
            IsConnected = true;
            Ssid = "wlan0";
            SignalStrength = 100;
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
