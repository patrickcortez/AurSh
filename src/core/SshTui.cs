using System.Text;
using AurShell.Utils;

namespace AurShell.Core;

// TUI interface for SSH key and host management
public static class SshTui
{
    private enum Screen { MainMenu, Keys, Hosts }

    public static int Run(string workingDirectory)
    {
        // Enter alternate screen buffer
        Console.Write("\x1b[?1049h\x1b[?25l");

        try
        {
            var currentScreen = Screen.MainMenu;
            bool running = true;

            while (running)
            {
                switch (currentScreen)
                {
                    case Screen.MainMenu:
                        int menuResult = RunMainMenu();
                        if (menuResult == 0)
                        {
                            currentScreen = Screen.Keys;
                        }
                        else if (menuResult == 1)
                        {
                            currentScreen = Screen.Hosts;
                        }
                        else
                        {
                            running = false;
                        }
                        break;

                    case Screen.Keys:
                        RunKeysScreen();
                        currentScreen = Screen.MainMenu;
                        break;

                    case Screen.Hosts:
                        RunHostsScreen();
                        currentScreen = Screen.MainMenu;
                        break;
                }
            }
        }
        finally
        {
            // Restore main screen buffer
            Console.Write("\x1b[?25h\x1b[?1049l");
        }

        return 0;
    }

    // Main menu: returns 0=Keys, 1=Hosts, -1=Exit
    private static int RunMainMenu()
    {
        int selected = 0;
        string[] options = { "SSH-Keys", "Hosts" };

        while (true)
        {
            int width = Platform.TerminalWidth;
            int height = Platform.TerminalHeight;

            Console.Write(Ansi.SetCursorPosition(1, 1));

            // Clear and draw header
            DrawHeader("AurSh SSH Manager", width);

            // Center the menu vertically
            int menuStartRow = Math.Max(5, height / 2 - 2);

            // Draw the box
            int boxWidth = 36;
            int boxLeft = Math.Max(1, (width - boxWidth) / 2);

            for (int row = 0; row < height - 4; row++)
            {
                int screenRow = 4 + row;
                Console.Write($"\x1b[{screenRow};1H");
                Console.Write(Ansi.ClearLine);
            }

            // Top border
            string topBorder = "╭" + new string('─', boxWidth - 2) + "╮";
            Console.Write($"\x1b[{menuStartRow};{boxLeft}H");
            Console.Write(Ansi.FgBrightCyan + topBorder + Ansi.Reset);

            // Empty line
            Console.Write($"\x1b[{menuStartRow + 1};{boxLeft}H");
            Console.Write(Ansi.FgBrightCyan + "│" + new string(' ', boxWidth - 2) + "│" + Ansi.Reset);

            // Menu options
            for (int i = 0; i < options.Length; i++)
            {
                int optionRow = menuStartRow + 2 + i;
                string label = $"[   {options[i]}   ]";
                int labelLeft = boxLeft + (boxWidth - label.Length) / 2;

                Console.Write($"\x1b[{optionRow};{boxLeft}H");
                Console.Write(Ansi.FgBrightCyan + "│" + Ansi.Reset);

                // Padding before label
                Console.Write(new string(' ', labelLeft - boxLeft - 1));

                if (i == selected)
                {
                    Console.Write(Ansi.BgRgb(50, 50, 70) + Ansi.FgWhite + Ansi.Bold + label + Ansi.Reset);
                }
                else
                {
                    Console.Write(Ansi.FgRgb(200, 200, 200) + label + Ansi.Reset);
                }

                // Padding after label
                int remaining = boxWidth - 2 - (labelLeft - boxLeft - 1) - label.Length;
                if (remaining > 0)
                {
                    Console.Write(new string(' ', remaining));
                }

                Console.Write(Ansi.FgBrightCyan + "│" + Ansi.Reset);
            }

            // Empty line
            int emptyRow = menuStartRow + 2 + options.Length;
            Console.Write($"\x1b[{emptyRow};{boxLeft}H");
            Console.Write(Ansi.FgBrightCyan + "│" + new string(' ', boxWidth - 2) + "│" + Ansi.Reset);

            // Bottom border
            string bottomBorder = "╰" + new string('─', boxWidth - 2) + "╯";
            Console.Write($"\x1b[{emptyRow + 1};{boxLeft}H");
            Console.Write(Ansi.FgBrightCyan + bottomBorder + Ansi.Reset);

            // Status bar
            Console.Write($"\x1b[{height};1H");
            Console.Write(Ansi.ClearLine);
            Console.Write(Ansi.FgRgb(100, 100, 130) + "  ↑/↓ Navigate  Enter Select  Esc Exit" + Ansi.Reset);

            var key = Console.ReadKey(true);

            if (key.Key == ConsoleKey.Escape ||
                (key.Key == ConsoleKey.C && (key.Modifiers & ConsoleModifiers.Control) != 0))
            {
                return -1;
            }

            if (key.Key == ConsoleKey.Enter)
            {
                return selected;
            }

            if (key.Key == ConsoleKey.UpArrow || key.Key == ConsoleKey.Tab)
            {
                selected = selected == 0 ? options.Length - 1 : selected - 1;
            }

            if (key.Key == ConsoleKey.DownArrow)
            {
                selected = (selected + 1) % options.Length;
            }
        }
    }

    // SSH Keys list screen
    private static void RunKeysScreen()
    {
        int selectedIndex = 0;
        int scrollOffset = 0;
        bool running = true;

        var keys = SshKeyDiscovery.DiscoverKeys(Platform.SshDirectory);

        while (running)
        {
            int width = Platform.TerminalWidth;
            int height = Platform.TerminalHeight;
            int listHeight = height - 6;

            if (keys.Count > 0)
            {
                if (selectedIndex < 0) { selectedIndex = 0; }
                if (selectedIndex >= keys.Count) { selectedIndex = keys.Count - 1; }
                if (selectedIndex < scrollOffset) { scrollOffset = selectedIndex; }
                if (selectedIndex >= scrollOffset + listHeight) { scrollOffset = selectedIndex - listHeight + 1; }
            }

            Console.Write(Ansi.SetCursorPosition(1, 1));
            DrawHeader("SSH Keys", width);

            // Column header
            Console.Write($"\x1b[4;1H");
            Console.Write(Ansi.ClearLine);
            string colHeader = $"  {"Name",-20}  {"Type",-12}  {"Fingerprint"}";
            Console.Write(Ansi.FgBrightBlack + colHeader + Ansi.Reset);
            Console.WriteLine();

            for (int i = 0; i < listHeight; i++)
            {
                int screenRow = 5 + i;
                Console.Write($"\x1b[{screenRow};1H");
                Console.Write(Ansi.ClearLine);

                int itemIdx = scrollOffset + i;
                if (itemIdx < keys.Count)
                {
                    var entry = keys[itemIdx];
                    bool isSelected = itemIdx == selectedIndex;

                    string name = TruncateString(entry.Name, 20);
                    string type = TruncateString(entry.Type, 12);
                    int fpMaxLen = Math.Max(10, width - 40);
                    string fingerprint = TruncateString(entry.Fingerprint, fpMaxLen);

                    string line = $"  {name,-20}  {type,-12}  {fingerprint}";

                    if (isSelected)
                    {
                        Console.Write(Ansi.BgRgb(50, 50, 70) + Ansi.FgWhite + line.PadRight(width - 1) + Ansi.Reset);
                    }
                    else
                    {
                        Console.Write(Ansi.FgBrightCyan + $"  {name,-20}" + Ansi.Reset +
                                      Ansi.FgRgb(200, 200, 200) + $"  {type,-12}" + Ansi.Reset +
                                      Ansi.FgBrightBlack + $"  {fingerprint}" + Ansi.Reset);
                    }
                }
            }

            // Status bar
            Console.Write($"\x1b[{height};1H");
            Console.Write(Ansi.ClearLine);
            string status = keys.Count == 0
                ? "  No SSH keys found in ~/.ssh/"
                : $"  {keys.Count} key(s)  |  Ctrl+N New  Ctrl+D Delete  Esc Back";
            Console.Write(Ansi.FgRgb(100, 100, 130) + status + Ansi.Reset);

            var key = Console.ReadKey(true);

            if (key.Key == ConsoleKey.Escape ||
                (key.Key == ConsoleKey.C && (key.Modifiers & ConsoleModifiers.Control) != 0))
            {
                running = false;
            }
            else if (key.Key == ConsoleKey.UpArrow)
            {
                if (selectedIndex > 0) { selectedIndex--; }
            }
            else if (key.Key == ConsoleKey.DownArrow)
            {
                if (selectedIndex < keys.Count - 1) { selectedIndex++; }
            }
            else if (key.Key == ConsoleKey.PageUp)
            {
                selectedIndex = Math.Max(0, selectedIndex - listHeight);
            }
            else if (key.Key == ConsoleKey.PageDown)
            {
                selectedIndex = Math.Min(keys.Count - 1, selectedIndex + listHeight);
            }
            else if (key.Key == ConsoleKey.N && (key.Modifiers & ConsoleModifiers.Control) != 0)
            {
                GenerateNewKey();
                keys = SshKeyDiscovery.DiscoverKeys(Platform.SshDirectory);
            }
            else if (key.Key == ConsoleKey.D && (key.Modifiers & ConsoleModifiers.Control) != 0)
            {
                if (keys.Count > 0 && selectedIndex < keys.Count)
                {
                    if (ConfirmAction($"Delete key '{keys[selectedIndex].Name}'?"))
                    {
                        DeleteKey(keys[selectedIndex]);
                        keys = SshKeyDiscovery.DiscoverKeys(Platform.SshDirectory);
                        if (selectedIndex >= keys.Count && keys.Count > 0)
                        {
                            selectedIndex = keys.Count - 1;
                        }
                    }
                }
            }
        }
    }

    // Hosts list screen
    private static void RunHostsScreen()
    {
        int selectedIndex = 0;
        int scrollOffset = 0;
        bool running = true;
        string hostsPath = SshConfigStore.DefaultHostsPath;

        var hosts = SshConfigStore.Load(hostsPath);

        while (running)
        {
            int width = Platform.TerminalWidth;
            int height = Platform.TerminalHeight;
            int listHeight = height - 6;

            if (hosts.Count > 0)
            {
                if (selectedIndex < 0) { selectedIndex = 0; }
                if (selectedIndex >= hosts.Count) { selectedIndex = hosts.Count - 1; }
                if (selectedIndex < scrollOffset) { scrollOffset = selectedIndex; }
                if (selectedIndex >= scrollOffset + listHeight) { scrollOffset = selectedIndex - listHeight + 1; }
            }

            Console.Write(Ansi.SetCursorPosition(1, 1));
            DrawHeader("SSH Hosts", width);

            // Column header
            Console.Write($"\x1b[4;1H");
            Console.Write(Ansi.ClearLine);
            string colHeader = $"  {"Name",-20}  {"Connection"}";
            Console.Write(Ansi.FgBrightBlack + colHeader + Ansi.Reset);
            Console.WriteLine();

            for (int i = 0; i < listHeight; i++)
            {
                int screenRow = 5 + i;
                Console.Write($"\x1b[{screenRow};1H");
                Console.Write(Ansi.ClearLine);

                int itemIdx = scrollOffset + i;
                if (itemIdx < hosts.Count)
                {
                    var host = hosts[itemIdx];
                    bool isSelected = itemIdx == selectedIndex;

                    string name = TruncateString(host.Name, 20);
                    string connStr = FormatConnection(host);

                    string line = $"  {name,-20}  {connStr}";

                    if (isSelected)
                    {
                        Console.Write(Ansi.BgRgb(50, 50, 70) + Ansi.FgWhite + line.PadRight(width - 1) + Ansi.Reset);
                    }
                    else
                    {
                        Console.Write(Ansi.FgBrightCyan + $"  {name,-20}" + Ansi.Reset +
                                      Ansi.FgRgb(200, 200, 200) + $"  {connStr}" + Ansi.Reset);
                    }
                }
            }

            // Status bar
            Console.Write($"\x1b[{height};1H");
            Console.Write(Ansi.ClearLine);
            string status = hosts.Count == 0
                ? "  No hosts saved  |  Ctrl+N Add Host  Esc Back"
                : $"  {hosts.Count} host(s)  |  Enter Connect  Ctrl+N Add  Ctrl+E Edit  Ctrl+D Delete  Esc Back";
            Console.Write(Ansi.FgRgb(100, 100, 130) + status + Ansi.Reset);

            var key = Console.ReadKey(true);

            if (key.Key == ConsoleKey.Escape ||
                (key.Key == ConsoleKey.C && (key.Modifiers & ConsoleModifiers.Control) != 0))
            {
                running = false;
            }
            else if (key.Key == ConsoleKey.UpArrow)
            {
                if (selectedIndex > 0) { selectedIndex--; }
            }
            else if (key.Key == ConsoleKey.DownArrow)
            {
                if (selectedIndex < hosts.Count - 1) { selectedIndex++; }
            }
            else if (key.Key == ConsoleKey.PageUp)
            {
                selectedIndex = Math.Max(0, selectedIndex - listHeight);
            }
            else if (key.Key == ConsoleKey.PageDown)
            {
                selectedIndex = Math.Min(hosts.Count - 1, selectedIndex + listHeight);
            }
            else if (key.Key == ConsoleKey.Enter)
            {
                if (hosts.Count > 0 && selectedIndex < hosts.Count)
                {
                    ConnectToHost(hosts[selectedIndex]);
                }
            }
            else if (key.Key == ConsoleKey.N && (key.Modifiers & ConsoleModifiers.Control) != 0)
            {
                var newHost = PromptHostDetails(null);
                if (newHost != null)
                {
                    hosts.Add(newHost);
                    SshConfigStore.Save(hostsPath, hosts);
                }
            }
            else if (key.Key == ConsoleKey.E && (key.Modifiers & ConsoleModifiers.Control) != 0)
            {
                if (hosts.Count > 0 && selectedIndex < hosts.Count)
                {
                    var edited = PromptHostDetails(hosts[selectedIndex]);
                    if (edited != null)
                    {
                        hosts[selectedIndex] = edited;
                        SshConfigStore.Save(hostsPath, hosts);
                    }
                }
            }
            else if (key.Key == ConsoleKey.D && (key.Modifiers & ConsoleModifiers.Control) != 0)
            {
                if (hosts.Count > 0 && selectedIndex < hosts.Count)
                {
                    if (ConfirmAction($"Delete host '{hosts[selectedIndex].Name}'?"))
                    {
                        hosts.RemoveAt(selectedIndex);
                        SshConfigStore.Save(hostsPath, hosts);
                        if (selectedIndex >= hosts.Count && hosts.Count > 0)
                        {
                            selectedIndex = hosts.Count - 1;
                        }
                    }
                }
            }
        }
    }

    // Spawns ssh to connect to a host
    private static void ConnectToHost(SshHost host)
    {
        // Leave alternate buffer so SSH gets a clean terminal
        Console.Write("\x1b[?25h\x1b[?1049l");

        try
        {
            string connStr = $"{host.User}@{host.Host}";
            Console.WriteLine(Ansi.FgBrightCyan + $"  Connecting to {host.Name} ({connStr})..." + Ansi.Reset);

            var psi = new System.Diagnostics.ProcessStartInfo("ssh")
            {
                UseShellExecute = false,
                CreateNoWindow = false
            };

            psi.ArgumentList.Add($"{host.User}@{host.Host}");

            if (host.Port != 22)
            {
                psi.ArgumentList.Add("-p");
                psi.ArgumentList.Add(host.Port.ToString());
            }

            if (!string.IsNullOrEmpty(host.IdentityFile) && File.Exists(host.IdentityFile))
            {
                psi.ArgumentList.Add("-i");
                psi.ArgumentList.Add(host.IdentityFile);
            }

            using var proc = System.Diagnostics.Process.Start(psi);
            if (proc != null)
            {
                proc.WaitForExit();
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"aursh: aursh-ssh: connection failed: {ex.Message}");
            Console.Error.WriteLine("  Press any key to continue...");
            Console.ReadKey(true);
        }

        // Re-enter alternate buffer
        Console.Write("\x1b[?1049h\x1b[?25l");
    }

    // Generates a new SSH key via ssh-keygen
    private static void GenerateNewKey()
    {
        // Leave alternate buffer for ssh-keygen interaction
        Console.Write("\x1b[?25h\x1b[?1049l");

        try
        {
            Console.WriteLine();
            Console.Write(Ansi.FgBrightCyan + "  Key name: " + Ansi.Reset);
            string? keyName = Console.ReadLine()?.Trim();

            if (string.IsNullOrEmpty(keyName))
            {
                Console.WriteLine(Ansi.FgBrightYellow + "  Cancelled." + Ansi.Reset);
                WaitForKey();
                Console.Write("\x1b[?1049h\x1b[?25l");
                return;
            }

            // Validate key name: alphanumeric, underscore, dash only
            if (!IsValidKeyName(keyName))
            {
                Console.Error.WriteLine(Ansi.FgRed + "  Invalid key name. Use only letters, numbers, underscores and dashes." + Ansi.Reset);
                WaitForKey();
                Console.Write("\x1b[?1049h\x1b[?25l");
                return;
            }

            string keyPath = Path.Combine(Platform.SshDirectory, keyName);
            if (File.Exists(keyPath))
            {
                Console.Error.WriteLine(Ansi.FgRed + $"  Key '{keyName}' already exists." + Ansi.Reset);
                WaitForKey();
                Console.Write("\x1b[?1049h\x1b[?25l");
                return;
            }

            Console.Write(Ansi.FgBrightCyan + "  Comment (optional): " + Ansi.Reset);
            string? comment = Console.ReadLine()?.Trim();
            if (string.IsNullOrEmpty(comment))
            {
                comment = keyName;
            }

            // Ensure ~/.ssh/ exists
            if (!Directory.Exists(Platform.SshDirectory))
            {
                Directory.CreateDirectory(Platform.SshDirectory);
            }

            Console.WriteLine(Ansi.FgBrightBlack + "  Generating key..." + Ansi.Reset);

            var psi = new System.Diagnostics.ProcessStartInfo("ssh-keygen")
            {
                UseShellExecute = false,
                CreateNoWindow = false
            };

            psi.ArgumentList.Add("-t");
            psi.ArgumentList.Add("ed25519");
            psi.ArgumentList.Add("-f");
            psi.ArgumentList.Add(keyPath);
            psi.ArgumentList.Add("-C");
            psi.ArgumentList.Add(comment);

            using var proc = System.Diagnostics.Process.Start(psi);
            if (proc != null)
            {
                proc.WaitForExit();
                if (proc.ExitCode == 0)
                {
                    Console.WriteLine(Ansi.FgBrightGreen + $"  Key '{keyName}' generated." + Ansi.Reset);
                }
                else
                {
                    Console.Error.WriteLine(Ansi.FgRed + "  ssh-keygen failed." + Ansi.Reset);
                }
            }

            WaitForKey();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"aursh: aursh-ssh: key generation failed: {ex.Message}");
            WaitForKey();
        }

        // Re-enter alternate buffer
        Console.Write("\x1b[?1049h\x1b[?25l");
    }

    // Deletes a key pair (private + .pub)
    private static void DeleteKey(SshKeyEntry entry)
    {
        try
        {
            if (File.Exists(entry.PrivateKeyPath))
            {
                File.Delete(entry.PrivateKeyPath);
            }
            if (File.Exists(entry.PublicKeyPath))
            {
                File.Delete(entry.PublicKeyPath);
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"aursh: aursh-ssh: failed to delete key: {ex.Message}");
        }
    }

    // Prompts user for host details (add or edit)
    private static SshHost? PromptHostDetails(SshHost? existing)
    {
        // Leave alternate buffer for input
        Console.Write("\x1b[?25h\x1b[?1049l");

        try
        {
            string action = existing != null ? "Edit Host" : "New Host";
            Console.WriteLine();
            Console.WriteLine(Ansi.FgBrightCyan + $"  --- {action} ---" + Ansi.Reset);

            string defaultName = existing?.Name ?? "";
            string defaultUser = existing?.User ?? "";
            string defaultHost = existing?.Host ?? "";
            string defaultPort = existing?.Port.ToString() ?? "22";

            Console.Write(Ansi.FgBrightCyan + $"  Name [{defaultName}]: " + Ansi.Reset);
            string? name = Console.ReadLine()?.Trim();
            if (string.IsNullOrEmpty(name))
            {
                name = defaultName;
            }

            if (string.IsNullOrEmpty(name))
            {
                Console.WriteLine(Ansi.FgBrightYellow + "  Cancelled." + Ansi.Reset);
                WaitForKey();
                Console.Write("\x1b[?1049h\x1b[?25l");
                return null;
            }

            Console.Write(Ansi.FgBrightCyan + $"  User [{defaultUser}]: " + Ansi.Reset);
            string? user = Console.ReadLine()?.Trim();
            if (string.IsNullOrEmpty(user))
            {
                user = defaultUser;
            }

            if (string.IsNullOrEmpty(user))
            {
                Console.WriteLine(Ansi.FgBrightYellow + "  Cancelled." + Ansi.Reset);
                WaitForKey();
                Console.Write("\x1b[?1049h\x1b[?25l");
                return null;
            }

            Console.Write(Ansi.FgBrightCyan + $"  Host/IP [{defaultHost}]: " + Ansi.Reset);
            string? host = Console.ReadLine()?.Trim();
            if (string.IsNullOrEmpty(host))
            {
                host = defaultHost;
            }

            if (string.IsNullOrEmpty(host))
            {
                Console.WriteLine(Ansi.FgBrightYellow + "  Cancelled." + Ansi.Reset);
                WaitForKey();
                Console.Write("\x1b[?1049h\x1b[?25l");
                return null;
            }

            Console.Write(Ansi.FgBrightCyan + $"  Port [{defaultPort}]: " + Ansi.Reset);
            string? portStr = Console.ReadLine()?.Trim();
            if (string.IsNullOrEmpty(portStr))
            {
                portStr = defaultPort;
            }

            if (!int.TryParse(portStr, out int port) || port < 1 || port > 65535)
            {
                Console.Error.WriteLine(Ansi.FgRed + "  Invalid port number." + Ansi.Reset);
                WaitForKey();
                Console.Write("\x1b[?1049h\x1b[?25l");
                return null;
            }

            var result = new SshHost
            {
                Name = name,
                User = user,
                Host = host,
                Port = port,
                IdentityFile = existing?.IdentityFile ?? ""
            };

            Console.WriteLine(Ansi.FgBrightGreen + $"  Saved: {name} → {user}@{host}:{port}" + Ansi.Reset);
            WaitForKey();
            Console.Write("\x1b[?1049h\x1b[?25l");
            return result;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"aursh: aursh-ssh: {ex.Message}");
            WaitForKey();
            Console.Write("\x1b[?1049h\x1b[?25l");
            return null;
        }
    }

    // Confirmation prompt at the bottom of the screen
    private static bool ConfirmAction(string message)
    {
        int height = Platform.TerminalHeight;
        Console.Write($"\x1b[{height};1H");
        Console.Write(Ansi.ClearLine);
        Console.Write(Ansi.FgBrightYellow + $"  {message} (y/N) " + Ansi.Reset);
        Console.Write("\x1b[?25h");

        var key = Console.ReadKey(true);

        Console.Write("\x1b[?25l");
        return key.KeyChar == 'y' || key.KeyChar == 'Y';
    }

    // Shared header drawing
    private static void DrawHeader(string title, int width)
    {
        Console.Write(Ansi.ClearLine);
        Console.WriteLine($"\n  {Ansi.FgBrightCyan}{Ansi.Bold}{title}{Ansi.Reset}");

        Console.Write(Ansi.ClearLine);
        Console.WriteLine(Ansi.FgBrightBlack + new string('─', width) + Ansi.Reset);
    }

    // Formats host connection string for display
    private static string FormatConnection(SshHost host)
    {
        string conn = $"{host.User}@{host.Host}";
        if (host.Port != 22)
        {
            conn += $":{host.Port}";
        }
        return conn;
    }

    // Truncates string with ellipsis if needed
    private static string TruncateString(string value, int maxLen)
    {
        if (string.IsNullOrEmpty(value))
        {
            return "";
        }
        if (value.Length <= maxLen)
        {
            return value;
        }
        return value.Substring(0, maxLen - 1) + "…";
    }

    // Validates a key name (alphanumeric, underscore, dash)
    private static bool IsValidKeyName(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return false;
        }

        foreach (char c in name)
        {
            if (!char.IsLetterOrDigit(c) && c != '_' && c != '-')
            {
                return false;
            }
        }

        return true;
    }

    private static void WaitForKey()
    {
        Console.Write(Ansi.FgBrightBlack + "  Press any key to continue..." + Ansi.Reset);
        Console.ReadKey(true);
    }
}
