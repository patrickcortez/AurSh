using System.IO;
using AurShell.Utils;

namespace AurShell.BlackBoxView;

/// <summary>
/// Dynamically determines which commands should bypass the BlackBox
/// viewport and run with full terminal control.
///
/// Bypass sources (checked in order, results cached at first use):
///
///  1. The static TUI list in <see cref="BlackBoxConfig.Bypass"/>
///     (env var BLACKBOX_BYPASS override). Contains well-known TUI
///     programs that can't function inside a box (vim, less, htop...).
///
///  2. The user's persistent bypass file at ~/.aursh/Bypass.txt — one
///     program name per line, '#' for comments, blank lines ignored.
///
///  3. System shells discovered at runtime:
///     - POSIX: every entry in /etc/shells.
///     - Windows: probes PATH for powershell, pwsh, cmd, wsl, bash,
///       zsh, fish, nu, elvish, and any shell binaries from MSYS2/Git.
///     - Termux: /etc/shells + /data/data/com.termux prefix shells.
///
/// The cache is populated lazily on the first IsBypassed() call and
/// stays for the lifetime of the process. A shell restart picks up
/// newly installed shells.
/// </summary>
public static class BypassList
{
    private static HashSet<string>? _dynamicCache;
    private static HashSet<string>? _userBypassCache;
    private static readonly object _lock = new();

    /// <summary>
    /// True for commands that should NOT have a BlackBox around them at all:
    /// fullscreen TUI programs (vim, top, less, htop, tmux, …) and shells
    /// (powershell, bash, zsh, wsl, …). They take over the whole terminal
    /// and a wrapping box would corrupt the display or break their input.
    /// </summary>
    public static bool IsBypassed(string commandName, BlackBoxConfig config)
    {
        if (string.IsNullOrWhiteSpace(commandName)) return false;
        string name = Path.GetFileNameWithoutExtension(commandName).ToLowerInvariant();

        // 1. Static TUI bypass list from config / BLACKBOX_BYPASS env var
        foreach (string b in config.Bypass)
        {
            if (string.Equals(b.Trim(), name, System.StringComparison.OrdinalIgnoreCase))
                return true;
        }

        // 2. User's persistent bypass file (~/.aursh/Bypass.txt)
        if (IsInUserBypassFile(name))
            return true;

        // 3. Dynamically discovered system shells
        if (IsSystemShell(name))
            return true;

        return false;
    }

    /// <summary>
    /// True for commands that should run in *passthrough* mode: the BlackBox
    /// header and footer frame the command, but the child inherits the real
    /// terminal stdio so its TTY-dependent behavior (python REPL prompt,
    /// arrow-key history, password prompts, progress bars) works normally.
    ///
    /// Currently used for interactive REPLs on platforms without a usable
    /// pseudo-terminal (Windows, since there's no ConPTY bridge yet). On POSIX
    /// the regular /usr/bin/script PTY wrap is preferred, so this returns false.
    /// </summary>
    public static bool NeedsPassthrough(string commandName, BlackBoxConfig config)
    {
        if (string.IsNullOrWhiteSpace(commandName)) return false;
        string name = Path.GetFileNameWithoutExtension(commandName).ToLowerInvariant();

        if (IsBypassed(name, config))
            return false;

        if (!IsInteractiveCommand(name)) return false;
        return !PtyHost.IsAvailable();
    }

    /// <summary>
    /// Returns all dynamically discovered system shells. Useful for
    /// diagnostics and the bypass debug command.
    /// </summary>
    public static IReadOnlyCollection<string> GetDiscoveredShells()
    {
        EnsureCachePopulated();
        lock (_lock) { return _dynamicCache!.ToArray(); }
    }

    /// <summary>
    /// Returns all user-configured bypass entries from ~/.aursh/Bypass.txt.
    /// </summary>
    public static IReadOnlyCollection<string> GetUserBypassEntries()
    {
        EnsureUserBypassPopulated();
        lock (_lock) { return _userBypassCache!.ToArray(); }
    }

    private static bool IsInteractiveCommand(string basename)
    {

        return PtyHost.NeedsPty(basename);
    }

    private static bool IsSystemShell(string basename)
    {
        EnsureCachePopulated();
        lock (_lock) { return _dynamicCache!.Contains(basename); }
    }

    private static bool IsInUserBypassFile(string basename)
    {
        EnsureUserBypassPopulated();
        lock (_lock) { return _userBypassCache!.Contains(basename); }
    }

    private static void EnsureCachePopulated()
    {
        if (_dynamicCache != null) return;
        lock (_lock)
        {
            if (_dynamicCache != null) return;
            _dynamicCache = DiscoverSystemShells();
        }
    }

    private static void EnsureUserBypassPopulated()
    {
        if (_userBypassCache != null) return;
        lock (_lock)
        {
            if (_userBypassCache != null) return;
            _userBypassCache = LoadUserBypassFile();
        }
    }

    /// <summary>
    /// Discovers shells installed on the system by reading OS-level
    /// registries and probing PATH. Results are platform-specific:
    ///
    /// POSIX / Termux:
    ///   Parses /etc/shells — the POSIX-standard file listing every
    ///   valid login shell. Each non-comment, non-empty line is an
    ///   absolute path; we extract the basename without extension.
    ///
    /// Windows:
    ///   No /etc/shells equivalent exists. We probe PATH for known
    ///   shell executables (cmd, powershell, pwsh, wsl) and also
    ///   detect shells installed via MSYS2, Git for Windows, or
    ///   Cygwin by checking their typical install paths.
    ///
    /// All platforms:
    ///   If the SHELL or COMSPEC environment variable is set, the
    ///   basename of its value is added.
    /// </summary>
    private static HashSet<string> DiscoverSystemShells()
    {
        var shells = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);

        // Cross-platform: honor SHELL and COMSPEC env vars
        AddFromEnvVar(shells, "SHELL");
        AddFromEnvVar(shells, "COMSPEC");

        if (Platform.IsUnixLike)
            DiscoverPosixShells(shells);
        else
            DiscoverWindowsShells(shells);

        return shells;
    }

    private static void DiscoverPosixShells(HashSet<string> shells)
    {

        string shellsFile = "/etc/shells";

        // Termux may also have shells at a non-standard prefix
        if (Platform.CurrentOS == OperatingSystemType.Termux)
        {
            string? prefix = System.Environment.GetEnvironmentVariable("PREFIX");
            if (!string.IsNullOrEmpty(prefix))
            {
                string termuxShells = Path.Combine(prefix, "etc", "shells");
                ParseShellsFile(shells, termuxShells);
            }
        }

        ParseShellsFile(shells, shellsFile);
    }

    private static void ParseShellsFile(HashSet<string> shells, string path)
    {
        try
        {
            if (!File.Exists(path)) return;
            foreach (string rawLine in File.ReadAllLines(path))
            {
                string line = rawLine.Trim();
                if (string.IsNullOrEmpty(line) || line[0] == '#')
                    continue;

                // Lines are absolute paths like /bin/bash, /usr/bin/zsh
                string basename = Path.GetFileNameWithoutExtension(line);
                if (!string.IsNullOrEmpty(basename))
                    shells.Add(basename);
            }
        }
        catch { }
    }

    private static void DiscoverWindowsShells(HashSet<string> shells)
    {
        // cmd.exe is always present on Windows
        shells.Add("cmd");

        // Probe PATH for shells that may or may not be installed
        string[] candidates = {
            "powershell", "pwsh",
            "wsl",
            "bash", "sh", "zsh", "fish",
            "dash", "ksh", "csh", "tcsh",
            "nu", "nushell", "elvish", "xonsh"
        };

        foreach (string name in candidates)
        {
            if (Platform.FindExecutableInPath(name) != null)
                shells.Add(name);
        }

        // Check well-known install paths for shells that may not be in PATH
        // Git for Windows / MSYS2 / Cygwin
        string[] knownPrefixes = {
            Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.ProgramFiles), "Git", "bin"),
            Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.ProgramFiles), "Git", "usr", "bin"),
            @"C:\msys64\usr\bin",
            @"C:\cygwin64\bin",
            @"C:\cygwin\bin"
        };

        foreach (string prefix in knownPrefixes)
        {
            try
            {
                if (!Directory.Exists(prefix)) continue;
                foreach (string exe in Directory.GetFiles(prefix, "*.exe"))
                {
                    string exeName = Path.GetFileNameWithoutExtension(exe).ToLowerInvariant();
                    if (IsLikelyShell(exeName))
                        shells.Add(exeName);
                }
            }
            catch { }
        }
    }

    /// <summary>
    /// Heuristic: an executable found in a shell install directory is
    /// likely a shell if its name matches known shell basename patterns.
    /// This avoids adding every utility in /usr/bin as a "shell".
    /// </summary>
    private static bool IsLikelyShell(string basename)
    {
        // Definitive shell names
        return basename switch
        {
            "bash" or "sh" or "dash" or "zsh" or "fish" or
            "ksh" or "ksh93" or "mksh" or "oksh" or
            "csh" or "tcsh" or
            "ash" or "busybox" or
            "pwsh" or "powershell" or "cmd" or
            "nu" or "nushell" or "elvish" or "xonsh" or
            "oil" or "osh" or "yash" or "rc" or "es" => true,
            _ => false
        };
    }

    private static void AddFromEnvVar(HashSet<string> shells, string envVar)
    {
        string? value = System.Environment.GetEnvironmentVariable(envVar);
        if (string.IsNullOrEmpty(value)) return;
        string basename = Path.GetFileNameWithoutExtension(value);
        if (!string.IsNullOrEmpty(basename))
            shells.Add(basename);
    }

    /// <summary>
    /// Reads the user's persistent bypass file at ~/.aursh/Bypass.txt.
    /// Format: one program name per line, '#' for comments, blank lines
    /// ignored. Lines can be basenames (no extension) or full paths
    /// (the basename is extracted).
    /// </summary>
    private static HashSet<string> LoadUserBypassFile()
    {
        var entries = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
        string filePath = Path.Combine(Platform.HomeDirectory, ".aursh", "Bypass.txt");

        try
        {
            if (!File.Exists(filePath)) return entries;
            foreach (string rawLine in File.ReadAllLines(filePath))
            {
                string line = rawLine.Trim();
                if (string.IsNullOrEmpty(line) || line[0] == '#')
                    continue;

                // Support both basenames ("mycustomtui") and paths ("/usr/local/bin/mytui")
                string basename = Path.GetFileNameWithoutExtension(line);
                if (!string.IsNullOrEmpty(basename))
                    entries.Add(basename);
            }
        }
        catch { }

        return entries;
    }
}
