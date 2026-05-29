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
    }

    private static List<WifiNetwork> GetNetworks()
    {
        List<WifiNetwork> networks = new List<WifiNetwork>();
        try
        {
            if (Platform.CurrentOS == OperatingSystemType.Windows)
            {
                string output = RunCommand("netsh", "wlan show networks mode=bssid");
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
                        if (idx >= 0) current.Ssid = t.Substring(idx + 1).Trim();
                    }
                    else if (t.StartsWith("Authentication") && current != null)
                    {
                        int idx = t.IndexOf(':');
                        if (idx >= 0) current.Security = t.Substring(idx + 1).Trim();
                    }
                    else if (t.StartsWith("Signal") && current != null)
                    {
                        int idx = t.IndexOf(':');
                        if (idx >= 0) current.Signal = t.Substring(idx + 1).Trim();
                    }
                }
                if (current != null && !string.IsNullOrEmpty(current.Ssid))
                {
                    networks.Add(current);
                }
            }
            else if (Platform.CurrentOS == OperatingSystemType.Linux)
            {
                string output = RunCommand("nmcli", "-t -f ssid,signal,security dev wifi list");
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
            else if (Platform.CurrentOS == OperatingSystemType.MacOS)
            {
                string output = RunCommand("/System/Library/PrivateFrameworks/Apple80211.framework/Versions/Current/Resources/airport", "-s");
                bool headerPassed = false;
                foreach (string line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                {
                    if (!headerPassed)
                    {
                        if (line.Contains("SSID") && line.Contains("BSSID")) headerPassed = true;
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
        }
        catch { }
        
        return networks.GroupBy(n => n.Ssid).Select(g => g.First()).ToList();
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
                        if (netIdx == selectedIndex)
                        {
                            Console.Write(Ansi.FgBrightGreen + $"  > {net.Ssid,-25} [{net.Signal}] {net.Security}" + Ansi.Reset);
                        }
                        else
                        {
                            Console.Write(Ansi.FgWhite + $"    {net.Ssid,-25} [{net.Signal}] {net.Security}" + Ansi.Reset);
                        }
                    }
                }

                Console.Write($"\x1b[{height};1H" + Ansi.ClearLine + Ansi.FgBrightBlack + "  [UP/DOWN] Navigate  [ENTER] Connect  [ESC] Exit" + Ansi.Reset);

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
                    
                    Console.Write($"Enter password for '{networks[selectedIndex].Ssid}' (leave blank if none): ");
                    string? pwd = Console.ReadLine();
                    List<string> connArgs = new List<string> { networks[selectedIndex].Ssid };
                    if (!string.IsNullOrWhiteSpace(pwd))
                    {
                        connArgs.Add(pwd);
                    }
                    exitCode = Connect(connArgs);
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
        if (args.Count < 2)
        {
            Console.Error.WriteLine("Usage: aursh-net send <path-to-file/folder> <ip>");
            return 1;
        }

        string targetPath = FileSystem.ResolvePath(args[0], workingDirectory);
        string ip = args[1];

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
}
