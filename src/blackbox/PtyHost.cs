using System.Collections.Generic;
using System.Diagnostics;
using AurShell.Utils;

namespace AurShell.BlackBoxView;

/// <summary>
/// Optional PTY wrapper for interactive children (python REPL, ssh, apt, etc.).
/// Routes through /usr/bin/script on POSIX so the child believes its stdout is a TTY.
/// Windows is currently unsupported (no Pty.Net dep yet); falls back to plain pipes there.
/// </summary>
public static class PtyHost
{
    private static readonly System.Collections.Generic.HashSet<string> _defaultInteractive = new(System.StringComparer.OrdinalIgnoreCase)
    {
        "python", "python2", "python3", "ipython", "ipython2", "ipython3",
        "node", "deno", "bun",
        "ruby", "irb",
        "perl",
        "lua", "luajit",
        "ghci", "stack",
        "scala", "kotlinc",
        "ssh", "telnet", "ftp", "sftp",
        "mysql", "mariadb", "psql", "sqlite3", "sqlite", "redis-cli", "mongo", "mongosh",
        "gdb", "lldb", "pdb",
        "tclsh", "wish",
        "ocaml", "utop",
        "racket",
        "clojure", "clj",
        "guile",
        "swipl", "pl"
    };

    public static bool IsAvailable()
    {
        if (Platform.CurrentOS == OperatingSystemType.Windows) return false;

        string disable = System.Environment.GetEnvironmentVariable("AURSH_DISABLE_PTY") ?? "";
        if (disable == "1" || string.Equals(disable, "true", System.StringComparison.OrdinalIgnoreCase))
            return false;
        return System.IO.File.Exists("/usr/bin/script") || System.IO.File.Exists("/bin/script");
    }

    public static bool NeedsPty(string commandName)
    {
        if (string.IsNullOrWhiteSpace(commandName)) return false;
        string disable = System.Environment.GetEnvironmentVariable("AURSH_NO_PTY") ?? "";
        if (disable == "1" || string.Equals(disable, "true", System.StringComparison.OrdinalIgnoreCase))
            return false;

        string basename = System.IO.Path.GetFileNameWithoutExtension(commandName);
        string extra = System.Environment.GetEnvironmentVariable("AURSH_PTY") ?? "";
        if (!string.IsNullOrEmpty(extra))
        {
            foreach (string entry in extra.Split(',', ';', ' '))
            {
                if (string.Equals(entry.Trim(), basename, System.StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }

        return _defaultInteractive.Contains(basename);
    }

    /// <summary>
    /// Rewrite a command into a PTY-allocating launch via /usr/bin/script.
    /// Returns the new (executable, args) tuple. The original args are
    /// re-quoted into a single command string that script -c will evaluate.
    /// </summary>
    public static (string executable, System.Collections.Generic.IList<string> args) WrapForPty(
        string originalExecutable,
        System.Collections.Generic.IList<string> originalArgs)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append(ShellQuote(originalExecutable));
        foreach (string arg in originalArgs)
        {
            sb.Append(' ');
            sb.Append(ShellQuote(arg));
        }

        string scriptPath = System.IO.File.Exists("/usr/bin/script") ? "/usr/bin/script" : "/bin/script";
        var newArgs = new System.Collections.Generic.List<string>
        {
            "-qfec",
            sb.ToString(),
            "/dev/null"
        };
        return (scriptPath, newArgs);
    }

    private static string ShellQuote(string s)
    {
        if (string.IsNullOrEmpty(s)) return "''";
        if (System.Text.RegularExpressions.Regex.IsMatch(s, "^[A-Za-z0-9_./@%+:=,-]+$"))
            return s;
        return "'" + s.Replace("'", "'\\''") + "'";
    }
}
