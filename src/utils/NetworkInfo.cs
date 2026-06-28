using System;
using System.Diagnostics;
using System.Linq;
using System.Net.NetworkInformation;

namespace AurShell.Utils;

public static class NetworkInfo
{
    public static bool IsConnected { get; private set; } = false;
    public static bool HasInternet { get; private set; } = false;
    public static string Ssid { get; private set; } = string.Empty;
    public static int SignalStrength { get; private set; } = 0;
    public static int Bars { get; private set; } = 0;
    public static int LinkSpeed { get; private set; } = 0;
    public static bool IsWired { get; private set; } = false;

    private static DateTime _lastUpdate = DateTime.MinValue;
    private static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(2);
    private static System.Threading.Tasks.Task<bool>? _internetCheckTask;

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
            var activeAdapters = NetworkInterface.GetAllNetworkInterfaces()
                .Where(a => a.OperationalStatus == OperationalStatus.Up && IsRealConnection(a))
                .ToList();

            var wifiAdapter = activeAdapters.FirstOrDefault(a =>
                a.NetworkInterfaceType == NetworkInterfaceType.Wireless80211 ||
                a.Name.StartsWith("wlan", StringComparison.OrdinalIgnoreCase) ||
                a.Name.StartsWith("wifi", StringComparison.OrdinalIgnoreCase));

            var ethernetAdapter = activeAdapters.FirstOrDefault(a =>
                a != wifiAdapter &&
                (a.NetworkInterfaceType == NetworkInterfaceType.Ethernet ||
                 a.Name.StartsWith("eth", StringComparison.OrdinalIgnoreCase) ||
                 a.Name.StartsWith("en", StringComparison.OrdinalIgnoreCase)));

            if (wifiAdapter != null)
            {
                IsConnected = true;
                IsWired = false;
                Ssid = wifiAdapter.Name;
                SignalStrength = 100;

                try
                {
                    switch (Platform.CurrentOS)
                    {
                        case OperatingSystemType.Windows:
                            GetWindowsWifiInfo();
                            break;
                        case OperatingSystemType.Linux:
                            GetLinuxWifiInfo();
                            break;
                        case OperatingSystemType.MacOS:
                            GetMacOSWifiInfo();
                            break;
                        case OperatingSystemType.Termux:
                            GetTermuxWifiInfo();
                            break;
                    }
                }
                catch { }
            }
            else if (ethernetAdapter != null)
            {
                IsConnected = true;
                IsWired = true;
                Ssid = ethernetAdapter.Name;
                SignalStrength = 100;
            }
        }
        catch
        {
            IsConnected = false;
        }

        // Non-blocking internet check
        try
        {
            if (_internetCheckTask == null || _internetCheckTask.IsCompleted)
            {
                if (_internetCheckTask != null)
                {
                    HasInternet = _internetCheckTask.Result;
                }

                _internetCheckTask = System.Threading.Tasks.Task.Run(() => TryTcpFallback());
            }
        }
        catch
        {
            HasInternet = false;
        }

        // Final TCP fallback if nothing is found locally but internet is up
        if (!IsConnected && HasInternet)
        {
            IsConnected = true;
            IsWired = true;
            SignalStrength = 100;
            Ssid = "Connected";
        }

        if (IsConnected)
        {
            Bars = CalculateBars(SignalStrength);
        }

        _lastUpdate = DateTime.Now;
    }

    private static bool IsRealConnection(NetworkInterface adapter)
    {
        if (adapter.NetworkInterfaceType == NetworkInterfaceType.Loopback ||
            adapter.NetworkInterfaceType == NetworkInterfaceType.Tunnel)
        {
            return false;
        }

        try
        {
            var props = adapter.GetIPProperties();
            // A genuine internet or primary LAN connection will virtually always have a Default Gateway
            bool hasGateway = props.GatewayAddresses.Any(g => g.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork || g.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6);

            return hasGateway;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryTcpFallback()
    {
        try
        {
            using var client = new System.Net.Sockets.TcpClient();
            var connectTask = client.ConnectAsync("8.8.8.8", 53);
            bool success = connectTask.Wait(TimeSpan.FromMilliseconds(300));
            if (!success)
            {
                // Explicitly observe the exception that will happen when we dispose the client
                // to prevent an UnobservedTaskException from crashing the finalizer thread.
                connectTask.ContinueWith(t => _ = t.Exception, System.Threading.Tasks.TaskContinuationOptions.OnlyOnFaulted);
                return false;
            }
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void GetWindowsWifiInfo()
    {
        string output = RunCommand("netsh", "wlan show interfaces");
        if (!string.IsNullOrWhiteSpace(output))
        {
            foreach (string line in output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string trimLine = line.Trim();
                if (trimLine.StartsWith("SSID", StringComparison.OrdinalIgnoreCase) && !trimLine.StartsWith("BSSID", StringComparison.OrdinalIgnoreCase))
                {
                    int colonIndex = trimLine.IndexOf(':');
                    if (colonIndex >= 0 && colonIndex + 1 < trimLine.Length)
                        Ssid = trimLine.Substring(colonIndex + 1).Trim();
                }
                else if (trimLine.StartsWith("Signal", StringComparison.OrdinalIgnoreCase))
                {
                    int colonIndex = trimLine.IndexOf(':');
                    if (colonIndex >= 0 && colonIndex + 1 < trimLine.Length)
                    {
                        string signalStr = trimLine.Substring(colonIndex + 1).Trim().Replace("%", "");
                        if (int.TryParse(signalStr, out int sig))
                            SignalStrength = sig;
                    }
                }
                else if (trimLine.StartsWith("Receive rate", StringComparison.OrdinalIgnoreCase))
                {
                    int colonIndex = trimLine.IndexOf(':');
                    if (colonIndex >= 0 && colonIndex + 1 < trimLine.Length)
                    {
                        string speedStr = trimLine.Substring(colonIndex + 1).Trim();
                        if (double.TryParse(speedStr, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double speed))
                            LinkSpeed = (int)speed;
                    }
                }
            }
        }
    }

    private static void GetLinuxWifiInfo()
    {
        string output = RunCommand("nmcli", "-t -f active,ssid,signal,rate dev wifi");
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
                        SignalStrength = sig;
                }
                if (parts.Length >= 4)
                {
                    string rateStr = parts[3].Replace("Mbit/s", "").Trim();
                    if (int.TryParse(rateStr, out int rate))
                        LinkSpeed = rate;
                }
                return;
            }
        }
    }

    private static void GetMacOSWifiInfo()
    {
        string output = RunCommand("/System/Library/PrivateFrameworks/Apple80211.framework/Versions/Current/Resources/airport", "-I");
        if (string.IsNullOrWhiteSpace(output)) return;

        int rssi = -100;
        foreach (string line in output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
        {
            string trimLine = line.Trim();
            if (trimLine.StartsWith("SSID:"))
            {
                int colonIndex = trimLine.IndexOf(':');
                if (colonIndex >= 0 && colonIndex + 1 < trimLine.Length)
                    Ssid = trimLine.Substring(colonIndex + 1).Trim();
            }
            else if (trimLine.StartsWith("agrCtlRSSI:"))
            {
                int colonIndex = trimLine.IndexOf(':');
                if (colonIndex >= 0 && colonIndex + 1 < trimLine.Length)
                {
                    if (int.TryParse(trimLine.Substring(colonIndex + 1).Trim(), out int r))
                        rssi = r;
                }
            }
            else if (trimLine.StartsWith("lastTxRate:"))
            {
                int colonIndex = trimLine.IndexOf(':');
                if (colonIndex >= 0 && colonIndex + 1 < trimLine.Length)
                {
                    if (int.TryParse(trimLine.Substring(colonIndex + 1).Trim(), out int rate))
                        LinkSpeed = rate;
                }
            }
        }
        SignalStrength = Math.Max(0, Math.Min(100, 2 * (rssi + 100)));
    }

    private static void GetTermuxWifiInfo()
    {
        string output = RunCommand("termux-wifi-connectioninfo", "");
        if (!string.IsNullOrWhiteSpace(output))
        {
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
                        Ssid = parsedSsid;
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
                        SignalStrength = Math.Max(0, Math.Min(100, 2 * (rssi + 100)));
                }
            }

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
                        LinkSpeed = speed;
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

            process.WaitForExit(1000);
            return process.StandardOutput.ReadToEnd();
        }
        catch
        {
            return string.Empty;
        }
    }
}
