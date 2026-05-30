using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using AurShell.Utils;

namespace AurShell.Core;

public static class AurshNetCommand
{
    public static int Execute(CommandNode cmd, ShellEnvironment env, ref string workingDirectory)
    {
        if (cmd.Args.Count == 0)
        {
            return RunTui();
        }

        string subCmd = cmd.Args[0].ToLowerInvariant();
        List<string> subArgs = cmd.Args.Skip(1).ToList();

        switch (subCmd)
        {
            case "list":
                return RunListTui();
            case "connect":
                return Connect(subArgs);
            case "disconnect":
                return Disconnect();
            case "send":
                return SendFile(subArgs, workingDirectory);
            case "info":
                return ShowInfo();
            case "allow":
                return AllowIp(subArgs);
            case "disallow":
                return DisallowIp(subArgs);
            case "allowed":
                return ListAllowedIps();
            default:
                Console.Error.WriteLine($"aursh-net: Unknown command '{subCmd}'");
                return 1;
        }
    }

    private static int RunTui()
    {
        // A simple dashboard
        Console.Clear();
        Console.WriteLine(Ansi.FgBrightCyan + "  AurSh Net Dashboard  " + Ansi.Reset);
        Console.WriteLine("-----------------------");
        ShowInfo();
        Console.WriteLine();
        Console.WriteLine("Available commands: ");
        Console.WriteLine("  aursh-net list");
        Console.WriteLine("  aursh-net connect <ssid> [password]");
        Console.WriteLine("  aursh-net disconnect");
        Console.WriteLine("  aursh-net send <path> <ip>");
        Console.WriteLine("  aursh-net info");
        return 0;
    }

    private class WifiNetwork
    {
        public string Ssid { get; set; } = "";
        public string Signal { get; set; } = "";
        public string Security { get; set; } = "";
        public bool IsWired { get; set; } = false;
    }

    private static List<WifiNetwork> GetNetworks()
    {
        List<WifiNetwork> networks = new List<WifiNetwork>();
        try
        {
            if (Platform.CurrentOS == OperatingSystemType.Windows)
            {
                GetWindowsNetworks(networks);
            }
            else if (Platform.CurrentOS == OperatingSystemType.Linux)
            {
                GetLinuxNetworks(networks);
            }
            else if (Platform.CurrentOS == OperatingSystemType.MacOS)
            {
                GetMacOSNetworks(networks);
            }
            else if (Platform.CurrentOS == OperatingSystemType.Termux)
            {
                GetTermuxNetworks(networks);
            }
        }
        catch { }
        
        return networks.GroupBy(n => n.Ssid).Select(g => g.First()).ToList();
    }

    private static void GetWindowsNetworks(List<WifiNetwork> networks)
    {
        // I really hope this isn't a VM, but let's try the Wi-Fi scan first.
        string output = RunCommand("netsh", "wlan show networks mode=bssid");
        if (!string.IsNullOrWhiteSpace(output))
        {
            WifiNetwork? current = null;
            foreach (string line in output.Split('\n'))
            {
                string t = line.Trim();
                if (t.StartsWith("SSID "))
                {
                    if (current != null && !string.IsNullOrEmpty(current.Ssid))
                    {
                        networks.Add(current);
                    }
                    current = new WifiNetwork();
                    int idx = t.IndexOf(':');
                    if (idx >= 0)
                    {
                        current.Ssid = t.Substring(idx + 1).Trim();
                    }
                }
                else if (t.StartsWith("Authentication") && current != null)
                {
                    int idx = t.IndexOf(':');
                    if (idx >= 0)
                    {
                        current.Security = t.Substring(idx + 1).Trim();
                    }
                }
                else if (t.StartsWith("Signal") && current != null)
                {
                    int idx = t.IndexOf(':');
                    if (idx >= 0)
                    {
                        current.Signal = t.Substring(idx + 1).Trim();
                    }
                }
            }
            if (current != null && !string.IsNullOrEmpty(current.Ssid))
            {
                networks.Add(current);
            }
        }

        // Ugh, the wireless scan turned up empty. 
        // This is the absolute worst case: it's probably a VM pretending to have a bridged connection.
        // Let's go digging for wired interfaces.
        if (networks.Count == 0)
        {
            GetWindowsWiredInterfaces(networks);
        }
    }

    private static void GetWindowsWiredInterfaces(List<WifiNetwork> networks)
    {
        // Let's sift through the monstrosity that is ipconfig output.
        string ipOutput = RunCommand("ipconfig", "");
        if (string.IsNullOrWhiteSpace(ipOutput))
        {
            return;
        }

        string currentAdapter = string.Empty;

        foreach (string line in ipOutput.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
        {
            string trimLine = line.Trim();

            // Oh look, an un-indented line ending with a colon. That must be a new adapter!
            // Who came up with this formatting?!
            if (!line.StartsWith(" ") && !line.StartsWith("\t") && trimLine.EndsWith(":"))
            {
                currentAdapter = trimLine.TrimEnd(':');
                continue;
            }

            // YES! An IPv4 address! This adapter is actually doing something.
            if (trimLine.StartsWith("IPv4 Address", StringComparison.OrdinalIgnoreCase) ||
                trimLine.StartsWith("IPv4", StringComparison.OrdinalIgnoreCase))
            {
                int colonIdx = trimLine.IndexOf(':');
                if (colonIdx >= 0)
                {
                    string ipAddr = trimLine.Substring(colonIdx + 1).Trim();
                    // Don't you dare give me a loopback or APIPA address. Real connections only!
                    if (!ipAddr.StartsWith("127.") && !ipAddr.StartsWith("169.254.") && !string.IsNullOrEmpty(ipAddr))
                    {
                        if (!string.IsNullOrEmpty(currentAdapter))
                        {
                            networks.Add(new WifiNetwork
                            {
                                Ssid = currentAdapter,
                                Signal = "100%",
                                Security = "Wired",
                                IsWired = true
                            });
                        }
                    }
                }
            }
        }
    }

    private static void GetLinuxNetworks(List<WifiNetwork> networks)
    {
        // The happy path! nmcli usually just gives us what we want without a fight.
        string output = RunCommand("nmcli", "-t -f ssid,signal,security dev wifi list");
        if (!string.IsNullOrWhiteSpace(output))
        {
            foreach (string line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                string[] parts = line.Split(':');
                if (parts.Length >= 3 && !string.IsNullOrWhiteSpace(parts[0]))
                {
                    networks.Add(new WifiNetwork
                    {
                        Ssid = parts[0].Trim(),
                        Signal = parts[1].Trim() + "%",
                        Security = parts[2].Trim()
                    });
                }
            }
        }

        // Thank god, we found some Wi-Fi networks. We can stop right here.
        if (networks.Count > 0)
        {
            return;
        }

        // Are you kidding me? No Wi-Fi? Fine, let's ask nmcli if we're in a VM or using an ethernet cord.
        if (TryGetLinuxWiredFromNmcli(networks))
        {
            return;
        }

        // Alright, nmcli is dead. We have no choice but to dive into sysfs. Pray for us.
        TryGetLinuxWiredFromSysfs(networks);
    }

    private static bool TryGetLinuxWiredFromNmcli(List<WifiNetwork> networks)
    {
        // I hate that I have to query EVERY device just to find the wired ones.
        string output = RunCommand("nmcli", "-t -f device,type,state,connection dev");
        if (string.IsNullOrWhiteSpace(output))
        {
            return false;
        }

        bool foundAny = false;
        foreach (string line in output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
        {
            string[] parts = line.Split(':');
            if (parts.Length >= 4)
            {
                string devName = parts[0].Trim();
                string devType = parts[1].Trim();
                string devState = parts[2].Trim();
                string connName = parts[3].Trim();

                // I don't care about loopbacks or dead connections, and we ALREADY know Wi-Fi is dead.
                // Give me the real, active ethernet connections!
                if (devState.Equals("connected", StringComparison.OrdinalIgnoreCase) &&
                    !devType.Equals("wifi", StringComparison.OrdinalIgnoreCase) &&
                    !devType.Equals("loopback", StringComparison.OrdinalIgnoreCase))
                {
                    string displayName = !string.IsNullOrEmpty(connName) ? connName : devName;
                    networks.Add(new WifiNetwork
                    {
                        Ssid = displayName,
                        Signal = "100%",
                        Security = "Wired",
                        IsWired = true
                    });
                    foundAny = true;
                }
            }
        }

        return foundAny;
    }

    private static void TryGetLinuxWiredFromSysfs(List<WifiNetwork> networks)
    {
        // Reading the kernel's mind directly. It's rough out here.
        try
        {
            string netClassPath = "/sys/class/net";
            if (!Directory.Exists(netClassPath))
            {
                return;
            }

            foreach (string interfaceDir in Directory.GetDirectories(netClassPath))
            {
                string ifName = Path.GetFileName(interfaceDir);

                // Ignore the loopback. It always says it's connected, and it always lies.
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
                    bool isWireless = Directory.Exists(Path.Combine(interfaceDir, "wireless"));

                    networks.Add(new WifiNetwork
                    {
                        Ssid = ifName,
                        Signal = isWireless ? "?%" : "100%",
                        Security = isWireless ? "Wireless" : "Wired",
                        IsWired = !isWireless
                    });
                }
            }
        }
        catch
        {
            // It completely blew up. We must be in some insanely restrictive docker container. Oh well.
        }
    }

    private static void GetMacOSNetworks(List<WifiNetwork> networks)
    {
        string output = RunCommand("/System/Library/PrivateFrameworks/Apple80211.framework/Versions/Current/Resources/airport", "-s");
        bool headerPassed = false;
        foreach (string line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            if (!headerPassed)
            {
                if (line.Contains("SSID") && line.Contains("BSSID"))
                {
                    headerPassed = true;
                }
                continue;
            }
            if (line.Length > 32)
            {
                networks.Add(new WifiNetwork
                {
                    Ssid = line.Substring(0, 32).Trim(),
                    Signal = line.Substring(33, 10).Trim(),
                    Security = line.Substring(50).Trim()
                });
            }
        }
    }

    private static void GetTermuxNetworks(List<WifiNetwork> networks)
    {
        // Termux actually has a tool for this, thank goodness.
        // It's going to dump a giant JSON string on us, though.
        string output = RunCommand("termux-wifi-scanresults", "");
        if (string.IsNullOrWhiteSpace(output))
        {
            return;
        }

        // I'm not allowed to use a real JSON parser here. So I'm string slicing like it's 2005.
        // This is going to be messy.
        int searchStart = 0;
        while (searchStart < output.Length)
        {
            string ssidLabel = "\"ssid\": \"";
            int ssidIdx = output.IndexOf(ssidLabel, searchStart, StringComparison.Ordinal);
            if (ssidIdx < 0)
            {
                break;
            }

            int ssidStart = ssidIdx + ssidLabel.Length;
            int ssidEnd = output.IndexOf("\"", ssidStart, StringComparison.Ordinal);
            if (ssidEnd < 0)
            {
                break;
            }

            string ssid = output.Substring(ssidStart, ssidEnd - ssidStart);

            // Now let's try to dig out the signal strength.
            string levelLabel = "\"level\": ";
            int levelIdx = output.IndexOf(levelLabel, ssidEnd, StringComparison.Ordinal);
            string signal = "?%";
            if (levelIdx >= 0)
            {
                int levelStart = levelIdx + levelLabel.Length;
                int levelEnd = levelStart;
                while (levelEnd < output.Length && (char.IsDigit(output[levelEnd]) || output[levelEnd] == '-'))
                {
                    levelEnd++;
                }
                string levelStr = output.Substring(levelStart, levelEnd - levelStart);
                if (int.TryParse(levelStr, out int dbm))
                {
                    int percent = Math.Max(0, Math.Min(100, 2 * (dbm + 100)));
                    signal = percent + "%";
                }
            }

            // Last but not least, pry the security capabilities out of this text block.
            string capLabel = "\"capabilities\": \"";
            int capIdx = output.IndexOf(capLabel, ssidEnd, StringComparison.Ordinal);
            string security = "";
            if (capIdx >= 0)
            {
                int capStart = capIdx + capLabel.Length;
                int capEnd = output.IndexOf("\"", capStart, StringComparison.Ordinal);
                if (capEnd >= 0)
                {
                    security = output.Substring(capStart, capEnd - capStart);
                }
            }

            if (!string.IsNullOrEmpty(ssid) && ssid != "<unknown ssid>")
            {
                networks.Add(new WifiNetwork
                {
                    Ssid = ssid,
                    Signal = signal,
                    Security = security
                });
            }

            searchStart = ssidEnd + 1;
        }
    }

    private static int RunListTui()
    {
        Console.WriteLine(Ansi.FgBrightYellow + "Scanning for nearby networks..." + Ansi.Reset);
        List<WifiNetwork> networks = GetNetworks();

        if (networks.Count == 0)
        {
            Console.WriteLine(Ansi.FgRed + "No networks found or Wi-Fi is disabled." + Ansi.Reset);
            return 1;
        }

        Console.Write("\x1b[?1049h\x1b[?25l");
        int selectedIndex = 0;
        bool running = true;
        int exitCode = 0;

        try
        {
            while (running)
            {
                int height = Utils.Platform.TerminalHeight;
                Console.Write("\x1b[1;1H" + Ansi.ClearLine + Ansi.FgBrightCyan + "  AurSh Net - Select a Network" + Ansi.Reset);
                Console.Write("\x1b[2;1H" + Ansi.ClearLine + "  " + Ansi.FgBrightBlack + new string('-', 40) + Ansi.Reset);

                int maxDisplay = height - 4;
                int startIdx = Math.Max(0, selectedIndex - maxDisplay / 2);
                if (startIdx + maxDisplay > networks.Count)
                {
                    startIdx = Math.Max(0, networks.Count - maxDisplay);
                }

                for (int i = 0; i < maxDisplay; i++)
                {
                    int netIdx = startIdx + i;
                    Console.Write($"\x1b[{i + 3};1H" + Ansi.ClearLine);
                    if (netIdx < networks.Count)
                    {
                        var net = networks[netIdx];
                        // Show a wired icon for wired interfaces, wifi icon for wireless
                        string typeIcon = net.IsWired ? "\uF6FF" : "\uF1EB";
                        if (netIdx == selectedIndex)
                        {
                            Console.Write(Ansi.FgBrightGreen + $"  > {typeIcon} {net.Ssid,-25} [{net.Signal}] {net.Security}" + Ansi.Reset);
                        }
                        else
                        {
                            Console.Write(Ansi.FgWhite + $"    {typeIcon} {net.Ssid,-25} [{net.Signal}] {net.Security}" + Ansi.Reset);
                        }
                    }
                }

                // Adjust help text based on whether wired entries exist
                bool hasWireless = networks.Exists(n => !n.IsWired);
                string helpText = hasWireless
                    ? "  [UP/DOWN] Navigate  [ENTER] Connect  [ESC] Exit"
                    : "  [UP/DOWN] Navigate  [ENTER] View Info  [ESC] Exit";
                Console.Write($"\x1b[{height};1H" + Ansi.ClearLine + Ansi.FgBrightBlack + helpText + Ansi.Reset);

                var key = Console.ReadKey(true);
                if (key.Key == ConsoleKey.UpArrow && selectedIndex > 0)
                {
                    selectedIndex--;
                }
                else if (key.Key == ConsoleKey.DownArrow && selectedIndex < networks.Count - 1)
                {
                    selectedIndex++;
                }
                else if (key.Key == ConsoleKey.Escape || key.Key == ConsoleKey.Q)
                {
                    running = false;
                }
                else if (key.Key == ConsoleKey.Enter)
                {
                    running = false;
                    Console.Write("\x1b[?1049l\x1b[?25h");
                    
                    WifiNetwork selected = networks[selectedIndex];

                    if (selected.IsWired)
                    {
                        // Wired connections don't need passwords — just show info
                        Console.WriteLine(Ansi.FgBrightGreen + $"Active wired connection: {selected.Ssid}" + Ansi.Reset);
                        exitCode = ShowInfo();
                    }
                    else
                    {
                        // Wireless — ask for password and try connecting
                        Console.Write($"Enter password for '{selected.Ssid}' (leave blank if none): ");
                        string? pwd = Console.ReadLine();
                        List<string> connArgs = new List<string> { selected.Ssid };
                        if (!string.IsNullOrWhiteSpace(pwd))
                        {
                            connArgs.Add(pwd);
                        }
                        exitCode = Connect(connArgs);
                    }

                    return exitCode;
                }
            }
        }
        finally
        {
            if (running) 
            {
                Console.Write("\x1b[?1049l\x1b[?25h");
            }
        }

        return exitCode;
    }

    private static int Connect(List<string> args)
    {
        if (args.Count == 0)
        {
            Console.Error.WriteLine("aursh-net: connect requires an SSID");
            return 1;
        }

        string ssid = args[0];
        string password = args.Count > 1 ? args[1] : "";

        Console.WriteLine(Ansi.FgBrightYellow + $"Connecting to {ssid}..." + Ansi.Reset);

        string output = "";
        try
        {
            if (Platform.CurrentOS == OperatingSystemType.Windows)
            {
                // Windows netsh wlan connect requires a pre-existing profile.
                // We'll just run connect and if it fails, warn the user.
                output = RunCommand("netsh", $"wlan connect name=\"{ssid}\"");
            }
            else if (Platform.CurrentOS == OperatingSystemType.Linux)
            {
                if (string.IsNullOrEmpty(password))
                    output = RunCommand("nmcli", $"dev wifi connect \"{ssid}\"");
                else
                    output = RunCommand("nmcli", $"dev wifi connect \"{ssid}\" password \"{password}\"");
            }
            else if (Platform.CurrentOS == OperatingSystemType.MacOS)
            {
                if (string.IsNullOrEmpty(password))
                    output = RunCommand("networksetup", $"-setairportnetwork en0 \"{ssid}\"");
                else
                    output = RunCommand("networksetup", $"-setairportnetwork en0 \"{ssid}\" \"{password}\"");
            }
            
            Console.WriteLine(output.Trim());
            NetworkInfo.Refresh();
            if (NetworkInfo.IsConnected && NetworkInfo.Ssid == ssid)
            {
                Console.WriteLine(Ansi.FgBrightGreen + "Successfully connected." + Ansi.Reset);
                return 0;
            }
            return 1;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("aursh-net: " + ex.Message);
            return 1;
        }
    }

    private static int Disconnect()
    {
        Console.WriteLine(Ansi.FgBrightYellow + "Disconnecting..." + Ansi.Reset);
        try
        {
            string output = "";
            if (Platform.CurrentOS == OperatingSystemType.Windows)
            {
                output = RunCommand("netsh", "wlan disconnect");
            }
            else if (Platform.CurrentOS == OperatingSystemType.Linux)
            {
                output = RunCommand("nmcli", "dev disconnect wlan0");
            }
            else if (Platform.CurrentOS == OperatingSystemType.MacOS)
            {
                output = RunCommand("networksetup", "-setairportpower en0 off");
                RunCommand("networksetup", "-setairportpower en0 on");
            }
            Console.WriteLine(output.Trim());
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("aursh-net: " + ex.Message);
            return 1;
        }
    }

    private static int SendFile(List<string> args, string workingDirectory)
    {
        if (args.Count < 1)
        {
            Console.Error.WriteLine("Usage: aursh-net send <path-to-file/folder> [ip]");
            return 1;
        }

        string targetPath = FileSystem.ResolvePath(args[0], workingDirectory);
        string ip = string.Empty;

        if (args.Count >= 2)
        {
            ip = args[1];
        }
        else
        {
            Console.WriteLine(Ansi.FgBrightYellow + "Scanning local network for Aursh peers (2 seconds)..." + Ansi.Reset);
            var peers = AurshNetTransfer.DiscoverPeers();

            if (peers.Count == 0)
            {
                Console.Error.WriteLine(Ansi.FgRed + "No peers found on the local network. Make sure Aursh is running on the target device." + Ansi.Reset);
                return 1;
            }

            int selectedIndex = 0;
            bool running = true;

            Console.Write("\x1b[?1049h\x1b[?25l"); // Enter alt screen, hide cursor

            try
            {
                while (running)
                {
                    Console.Write("\x1b[H\x1b[2J"); // Clear screen
                    Console.WriteLine(Ansi.FgBrightCyan + "--- Select Target Device ---" + Ansi.Reset);
                    Console.WriteLine("Use Up/Down arrows to select, Enter to confirm, Esc to cancel.\n");

                    for (int i = 0; i < peers.Count; i++)
                    {
                        if (i == selectedIndex)
                        {
                            Console.WriteLine(Ansi.BgRgb(50, 60, 100) + Ansi.FgBrightWhite + $" > {peers[i].Hostname} ({peers[i].IPAddress})" + Ansi.Reset);
                        }
                        else
                        {
                            Console.WriteLine($"   {peers[i].Hostname} ({peers[i].IPAddress})");
                        }
                    }

                    var key = Console.ReadKey(true);
                    if (key.Key == ConsoleKey.UpArrow && selectedIndex > 0)
                    {
                        selectedIndex--;
                    }
                    else if (key.Key == ConsoleKey.DownArrow && selectedIndex < peers.Count - 1)
                    {
                        selectedIndex++;
                    }
                    else if (key.Key == ConsoleKey.Escape)
                    {
                        running = false;
                        return 0; // Cancelled
                    }
                    else if (key.Key == ConsoleKey.Enter)
                    {
                        running = false;
                        ip = peers[selectedIndex].IPAddress;
                    }
                }
            }
            finally
            {
                Console.Write("\x1b[?1049l\x1b[?25h"); // Exit alt screen, show cursor
            }
            
            if (string.IsNullOrEmpty(ip))
            {
                return 1; // Safety fallback
            }
        }

        Console.WriteLine(Ansi.FgBrightCyan + $"Sending to {ip}..." + Ansi.Reset);
        
        long lastReported = 0;
        
        try
        {
            AurshNetTransfer.Send(targetPath, ip, (file, sent, total) => 
            {
                long now = DateTimeOffset.Now.ToUnixTimeMilliseconds();
                if (now - lastReported > 200 || sent == total)
                {
                    double percent = total == 0 ? 100 : (sent * 100.0) / total;
                    Console.Write($"\rSending {file}: {sent}/{total} bytes ({percent:F1}%)   ");
                    lastReported = now;
                }
                
                if (sent == total)
                {
                    Console.WriteLine();
                }
            });

            Console.WriteLine(Ansi.FgBrightGreen + "Transfer complete." + Ansi.Reset);
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine();
            Console.Error.WriteLine(Ansi.FgRed + $"Transfer failed: {ex.Message}" + Ansi.Reset);
            return 1;
        }
    }

    private static int ShowInfo()
    {
        NetworkInfo.Refresh();
        
        string localIp = "Unknown";
        try
        {
            using (System.Net.Sockets.Socket socket = new System.Net.Sockets.Socket(System.Net.Sockets.AddressFamily.InterNetwork, System.Net.Sockets.SocketType.Dgram, 0))
            {
                socket.Connect("8.8.8.8", 65530);
                if (socket.LocalEndPoint is System.Net.IPEndPoint endPoint)
                {
                    localIp = endPoint.Address.ToString();
                }
            }
        }
        catch { }

        Console.WriteLine(Ansi.FgBrightMagenta + "--- Network Information ---" + Ansi.Reset);
        Console.WriteLine($"{Ansi.FgBrightBlue}Status:{Ansi.Reset}       {(NetworkInfo.IsConnected ? Ansi.FgBrightGreen + "Connected" : Ansi.FgRed + "Disconnected")}{Ansi.Reset}");
        if (NetworkInfo.IsConnected)
        {
            Console.WriteLine($"{Ansi.FgBrightBlue}SSID:{Ansi.Reset}         {NetworkInfo.Ssid}");
            Console.WriteLine($"{Ansi.FgBrightBlue}Strength:{Ansi.Reset}     {NetworkInfo.SignalStrength}% ({new string('|', NetworkInfo.Bars)}{new string('.', 4 - NetworkInfo.Bars)})");
        }
        Console.WriteLine($"{Ansi.FgBrightBlue}Local IP:{Ansi.Reset}     {localIp}");
        Console.WriteLine($"{Ansi.FgBrightBlue}Receiver:{Ansi.Reset}     Port 15333 {(NetworkInfo.IsConnected ? "Active" : "Waiting")}");
        Console.WriteLine(Ansi.FgBrightMagenta + "---------------------------" + Ansi.Reset);
        
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

            process.WaitForExit(5000); // 5 sec timeout
            return process.StandardOutput.ReadToEnd();
        }
        catch
        {
            return string.Empty;
        }
    }

    private static int AllowIp(List<string> args)
    {
        if (args.Count == 0)
        {
            Console.Error.WriteLine("Usage: aursh-net allow <ip-address>");
            return 1;
        }

        string ip = args[0];
        string file = AurshNetTransfer.EnsureAllowedIpFileExists();

        try
        {
            List<string> ips = File.ReadAllLines(file).ToList();

            if (ips.Contains(ip))
            {
                // Let's be conversational about it
                Console.WriteLine($"IP {ip} is already on the allowed list. No changes made.");
                return 0;
            }

            ips.Add(ip);
            File.WriteAllLines(file, ips);
            Console.WriteLine(Ansi.FgBrightGreen + $"Successfully added {ip} to the allowed list." + Ansi.Reset);
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(Ansi.FgRed + $"Failed to update whitelist: {ex.Message}" + Ansi.Reset);
            return 1;
        }
    }

    private static int DisallowIp(List<string> args)
    {
        if (args.Count == 0)
        {
            Console.Error.WriteLine("Usage: aursh-net disallow <ip-address>");
            return 1;
        }

        string ip = args[0];
        string file = AurshNetTransfer.EnsureAllowedIpFileExists();

        try
        {
            List<string> ips = File.ReadAllLines(file).ToList();
            if (ips.Remove(ip))
            {
                File.WriteAllLines(file, ips);
                Console.WriteLine(Ansi.FgBrightYellow + $"Removed {ip} from the allowed list." + Ansi.Reset);
            }
            else
            {
                Console.WriteLine($"IP {ip} was not found in the allowed list.");
            }
            
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(Ansi.FgRed + $"Failed to update whitelist: {ex.Message}" + Ansi.Reset);
            return 1;
        }
    }

    private static int ListAllowedIps()
    {
        string file = AurshNetTransfer.EnsureAllowedIpFileExists();

        try
        {
            string[] lines = File.ReadAllLines(file);
            bool hasIps = false;

            Console.WriteLine(Ansi.FgBrightCyan + "--- Allowed Incoming File Transfer IPs ---" + Ansi.Reset);
            foreach (string line in lines)
            {
                string cleanLine = line.Trim();
                if (!string.IsNullOrWhiteSpace(cleanLine) && !cleanLine.StartsWith("["))
                {
                    Console.WriteLine("  " + cleanLine);
                    hasIps = true;
                }
            }

            if (!hasIps)
            {
                Console.WriteLine(Ansi.FgBrightYellow + "The allowed list is empty. ALL incoming file transfers will be blocked." + Ansi.Reset);
            }

            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(Ansi.FgRed + $"Failed to read whitelist: {ex.Message}" + Ansi.Reset);
            return 1;
        }
    }
}
