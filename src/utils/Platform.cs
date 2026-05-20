using System.Runtime.InteropServices;

namespace AurShell.Utils;

public enum OperatingSystemType
{
    Windows,
    Linux,
    MacOS,
    Termux
}

public static class Platform
{
    private static OperatingSystemType? _cachedOs;
    private static string? _cachedHome;
    private static string? _cachedConfigDir;
    private static string? _cachedDataDir;

    public static OperatingSystemType CurrentOS
    {
        get
        {
            _cachedOs ??= DetectOperatingSystem();
            return _cachedOs.Value;
        }
    }

    public static string HomeDirectory
    {
        get
        {
            _cachedHome ??= ResolveHomeDirectory();
            return _cachedHome;
        }
    }

    public static string ConfigDirectory
    {
        get
        {
            _cachedConfigDir ??= ResolveConfigDirectory();
            return _cachedConfigDir;
        }
    }

    public static string DataDirectory
    {
        get
        {
            _cachedDataDir ??= ResolveDataDirectory();
            return _cachedDataDir;
        }
    }

    public static string HistoryFilePath => Path.Combine(DataDirectory, "history");
    public static string RcFilePath => Path.Combine(HomeDirectory, ".aurc");
    public static string SuggestionsDirectory => Path.Combine(HomeDirectory, ".aursh", "suggestions");
    public static char PathSeparator => CurrentOS == OperatingSystemType.Windows ? '\\' : '/';
    public static char PathListSeparator => CurrentOS == OperatingSystemType.Windows ? ';' : ':';
    public static string ExecutableExtension => CurrentOS == OperatingSystemType.Windows ? ".exe" : "";
    public static bool IsUnixLike => CurrentOS != OperatingSystemType.Windows;

    public static string[] ExecutableExtensions => CurrentOS == OperatingSystemType.Windows
        ? new[] { ".exe", ".cmd", ".bat", ".com", ".ps1" }
        : new[] { "" };

    public static string OsIcon => CurrentOS switch
    {
        OperatingSystemType.Windows => "\uE62A",
        OperatingSystemType.MacOS => "\uF179",
        OperatingSystemType.Linux => "\uF17C",
        OperatingSystemType.Termux => "\uF17B",
        _ => "?"
    };

    public static string OsName => CurrentOS switch
    {
        OperatingSystemType.Windows => "Windows",
        OperatingSystemType.MacOS => "macOS",
        OperatingSystemType.Linux => "Linux",
        OperatingSystemType.Termux => "Termux",
        _ => "Unknown"
    };

    public static string UserName
    {
        get
        {
            string? user = System.Environment.GetEnvironmentVariable("USER")
                           ?? System.Environment.GetEnvironmentVariable("USERNAME")
                           ?? System.Environment.GetEnvironmentVariable("LOGNAME");
            if (!string.IsNullOrEmpty(user))
                return user;

            if (IsUnixLike)
            {
                try
                {
                    var psi = new System.Diagnostics.ProcessStartInfo("whoami")
                    {
                        RedirectStandardOutput = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };
                    using var proc = System.Diagnostics.Process.Start(psi);
                    if (proc != null)
                    {
                        string output = proc.StandardOutput.ReadToEnd().Trim();
                        proc.WaitForExit();
                        if (!string.IsNullOrEmpty(output))
                            return output;
                    }
                }
                catch { }
            }
            return "user";
        }
    }

    public static string HostName
    {
        get
        {
            try { return System.Net.Dns.GetHostName(); }
            catch
            {
                string? h = System.Environment.GetEnvironmentVariable("HOSTNAME")
                            ?? System.Environment.GetEnvironmentVariable("COMPUTERNAME");
                return h ?? "localhost";
            }
        }
    }

    // Delegate to the TerminalSize cache so we don't call ioctl(TIOCGWINSZ)
    // on every read. TerminalSize.Start() is invoked from Program.Main and
    // keeps the value fresh via SIGWINCH + a low-frequency poll, so reads
    // here are O(1) memory loads. See src/utils/TerminalSize.cs.
    public static int TerminalWidth => TerminalSize.Width;

    public static int TerminalHeight => TerminalSize.Height;

    public static string DefaultShell => CurrentOS switch
    {
        OperatingSystemType.Windows => ResolveWindowsShell(),
        OperatingSystemType.Termux => "/data/data/com.termux/files/usr/bin/bash",
        _ => "/bin/bash"
    };

    public static string ShellFlag => "-c";

    public static void AddShellCommandArguments(System.Diagnostics.ProcessStartInfo psi, string command)
    {
        if (CurrentOS == OperatingSystemType.Windows)
        {
            string shellName = Path.GetFileName(psi.FileName);
            if (shellName.Equals("powershell.exe", StringComparison.OrdinalIgnoreCase))
            {
                psi.ArgumentList.Add("-NoProfile");
                psi.ArgumentList.Add("-ExecutionPolicy");
                psi.ArgumentList.Add("Bypass");
                psi.ArgumentList.Add("-Command");
                psi.ArgumentList.Add(command);
                return;
            }

            if (shellName.Equals("pwsh.exe", StringComparison.OrdinalIgnoreCase) ||
                shellName.Equals("pwsh", StringComparison.OrdinalIgnoreCase))
            {
                psi.ArgumentList.Add("-NoProfile");
                psi.ArgumentList.Add("-Command");
                psi.ArgumentList.Add(command);
                return;
            }
        }

        psi.ArgumentList.Add(ShellFlag);
        psi.ArgumentList.Add(command);
    }

    public static bool SupportsAnsi()
    {
        if (IsUnixLike)
            return true;
        if (!string.IsNullOrEmpty(System.Environment.GetEnvironmentVariable("WT_SESSION")))
            return true;
        if (!string.IsNullOrEmpty(System.Environment.GetEnvironmentVariable("ConEmuPID")))
            return true;
        if (!string.IsNullOrEmpty(System.Environment.GetEnvironmentVariable("TERM")))
            return true;
        return false;
    }

    public static void EnableAnsiOnWindows()
    {
        if (CurrentOS != OperatingSystemType.Windows)
            return;
        try
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;
            Console.InputEncoding = System.Text.Encoding.UTF8;
        }
        catch { }
    }

    public static string ExpandTilde(string path)
    {
        if (path == "~")
            return HomeDirectory;
        if (path.StartsWith("~/") || path.StartsWith("~\\"))
            return Path.Combine(HomeDirectory, path.Substring(2));
        return path;
    }

    public static string? FindExecutableInPath(string name)
    {
        if (Path.IsPathRooted(name) && File.Exists(name))
            return name;

        string? pathEnv = System.Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(pathEnv))
            return null;

        string[] dirs = pathEnv.Split(PathListSeparator, StringSplitOptions.RemoveEmptyEntries);
        foreach (string dir in dirs)
        {
            foreach (string ext in ExecutableExtensions)
            {
                string candidate = Path.Combine(dir, name + ext);
                if (File.Exists(candidate))
                    return candidate;
            }
        }
        return null;
    }

    public static string ShortenPath(string fullPath, int maxSegments = 3)
    {
        string display = fullPath;
        if (display.StartsWith(HomeDirectory, StringComparison.OrdinalIgnoreCase))
            display = "~" + display.Substring(HomeDirectory.Length);

        char sep = Path.DirectorySeparatorChar;
        string[] parts = display.Split(sep, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length <= maxSegments + 1)
            return display;

        string prefix = display.StartsWith("~") ? "~" : parts[0];
        string[] tail = parts.Skip(parts.Length - maxSegments).ToArray();
        return prefix + sep + "\u2026" + sep + string.Join(sep, tail);
    }

    public static void ApplyAndroidWorkarounds() // We set our termux env var before booting up
    {
        if (CurrentOS != OperatingSystemType.Termux && string.IsNullOrEmpty(System.Environment.GetEnvironmentVariable("ANDROID_ROOT")))
            return;

        if (string.IsNullOrEmpty(System.Environment.GetEnvironmentVariable("DOTNET_EnableWriteXorExecute")))
            System.Environment.SetEnvironmentVariable("DOTNET_EnableWriteXorExecute", "0");

        if (string.IsNullOrEmpty(System.Environment.GetEnvironmentVariable("DOTNET_GCHeapHardLimit")))
            System.Environment.SetEnvironmentVariable("DOTNET_GCHeapHardLimit", "1073741824");

        if (string.IsNullOrEmpty(System.Environment.GetEnvironmentVariable("DOTNET_GCRetainVM")))
            System.Environment.SetEnvironmentVariable("DOTNET_GCRetainVM", "1");

        if (string.IsNullOrEmpty(System.Environment.GetEnvironmentVariable("DOTNET_TieredCompilation")))
            System.Environment.SetEnvironmentVariable("DOTNET_TieredCompilation", "0");

        if (string.IsNullOrEmpty(System.Environment.GetEnvironmentVariable("DOTNET_TC_QuickJitForLoops")))
            System.Environment.SetEnvironmentVariable("DOTNET_TC_QuickJitForLoops", "0");

        if (string.IsNullOrEmpty(System.Environment.GetEnvironmentVariable("DOTNET_ReadyToRun")))
            System.Environment.SetEnvironmentVariable("DOTNET_ReadyToRun", "1");

        if (string.IsNullOrEmpty(System.Environment.GetEnvironmentVariable("DOTNET_GCConserveMemory")))
            System.Environment.SetEnvironmentVariable("DOTNET_GCConserveMemory", "9");

        int sdkVersion = DetectAndroidSdkVersion();
        if (sdkVersion >= 35)
        {
            if (string.IsNullOrEmpty(System.Environment.GetEnvironmentVariable("DOTNET_JitMinOpts")))
                System.Environment.SetEnvironmentVariable("DOTNET_JitMinOpts", "1");
        }
    }

    /// <summary>
    /// Reads the Android SDK version from the ANDROID_SDK_VERSION environment
    /// variable or from /system/build.prop (ro.build.version.sdk). Returns 0
    /// if the version cannot be determined.
    /// </summary>
    private static int DetectAndroidSdkVersion()
    {
        string? envSdk = System.Environment.GetEnvironmentVariable("ANDROID_SDK_VERSION");
        if (!string.IsNullOrEmpty(envSdk) && int.TryParse(envSdk, out int envVersion))
            return envVersion;

        try
        {
            string buildPropPath = "/system/build.prop";
            if (!File.Exists(buildPropPath))
                return 0;

            foreach (string line in File.ReadLines(buildPropPath))
            {
                string trimmed = line.Trim();
                if (trimmed.StartsWith("ro.build.version.sdk=", StringComparison.Ordinal))
                {
                    string value = trimmed.Substring("ro.build.version.sdk=".Length).Trim();
                    if (int.TryParse(value, out int sdkVer))
                        return sdkVer;
                }
            }
        }
        catch { }

        // Fallback: try getprop via process if build.prop wasn't readable
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo("getprop", "ro.build.version.sdk")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var proc = System.Diagnostics.Process.Start(psi);
            if (proc != null)
            {
                string output = proc.StandardOutput.ReadToEnd().Trim();
                proc.WaitForExit();
                if (int.TryParse(output, out int propVer))
                    return propVer;
            }
        }
        catch { }

        return 0;
    }

    public static void EnsureDirectoriesExist()
    {
        Directory.CreateDirectory(ConfigDirectory);
        Directory.CreateDirectory(DataDirectory);
        Directory.CreateDirectory(Path.Combine(DataDirectory, "sessions"));
        Directory.CreateDirectory(Path.Combine(HomeDirectory, ".aursh"));
        Directory.CreateDirectory(Path.Combine(HomeDirectory, ".aursh", "plugins"));
        Directory.CreateDirectory(SuggestionsDirectory);
    }

    private static OperatingSystemType DetectOperatingSystem()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return OperatingSystemType.Windows;
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return OperatingSystemType.MacOS;
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            if (Directory.Exists("/data/data/com.termux"))
                return OperatingSystemType.Termux;
            string? androidRoot = System.Environment.GetEnvironmentVariable("ANDROID_ROOT");
            if (!string.IsNullOrEmpty(androidRoot))
                return OperatingSystemType.Termux;
            string? prefix = System.Environment.GetEnvironmentVariable("PREFIX");
            if (!string.IsNullOrEmpty(prefix) && prefix.Contains("com.termux"))
                return OperatingSystemType.Termux;
            return OperatingSystemType.Linux;
        }
        return OperatingSystemType.Linux;
    }

    private static string ResolveHomeDirectory()
    {
        if (CurrentOS == OperatingSystemType.Termux)
        {
            string? h = System.Environment.GetEnvironmentVariable("HOME");
            return !string.IsNullOrEmpty(h) ? h : "/data/data/com.termux/files/home";
        }
        if (CurrentOS == OperatingSystemType.Windows)
        {
            string? up = System.Environment.GetEnvironmentVariable("USERPROFILE");
            if (!string.IsNullOrEmpty(up)) return up;
            string? hd = System.Environment.GetEnvironmentVariable("HOMEDRIVE");
            string? hp = System.Environment.GetEnvironmentVariable("HOMEPATH");
            if (!string.IsNullOrEmpty(hd) && !string.IsNullOrEmpty(hp)) return hd + hp;
        }
        string? home = System.Environment.GetEnvironmentVariable("HOME");
        if (!string.IsNullOrEmpty(home)) return home;
        return System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile);
    }

    private static string ResolveConfigDirectory()
    {
        if (CurrentOS == OperatingSystemType.Windows)
        {
            string? ad = System.Environment.GetEnvironmentVariable("APPDATA");
            if (!string.IsNullOrEmpty(ad)) return Path.Combine(ad, "aurshell");
        }
        string? xdg = System.Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        if (!string.IsNullOrEmpty(xdg)) return Path.Combine(xdg, "aurshell");
        return Path.Combine(HomeDirectory, ".config", "aurshell");
    }

    private static string ResolveDataDirectory()
    {
        if (CurrentOS == OperatingSystemType.Windows)
        {
            string? la = System.Environment.GetEnvironmentVariable("LOCALAPPDATA");
            if (!string.IsNullOrEmpty(la)) return Path.Combine(la, "aurshell");
        }
        string? xdg = System.Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        if (!string.IsNullOrEmpty(xdg)) return Path.Combine(xdg, "aurshell");
        return Path.Combine(HomeDirectory, ".local", "share", "aurshell");
    }

    private static string ResolveWindowsShell()
    {
        string? pwsh = FindExecutableInPath("pwsh");
        if (pwsh != null)
            return pwsh;

        string systemRoot = System.Environment.GetEnvironmentVariable("SystemRoot") ?? @"C:\Windows";
        string builtinPwsh = Path.Combine(systemRoot, "System32", "WindowsPowerShell", "v1.0", "powershell.exe");
        if (File.Exists(builtinPwsh))
            return builtinPwsh;

        return "powershell.exe";
    }
}
