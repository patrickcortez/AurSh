using System.Text;
using System.Text.Json;

namespace AurShell.Core;

public class SuggestionEntry
{
    public string Command { get; set; } = "";
    public List<string> Subcommands { get; set; } = new();
    public List<string> Flags { get; set; } = new();
}

public class SuggestionProvider
{
    private readonly Dictionary<string, SuggestionEntry> _entries = new(StringComparer.OrdinalIgnoreCase);
    private readonly string _suggestionsDirectory;

    public SuggestionProvider(string suggestionsDirectory)
    {
        _suggestionsDirectory = suggestionsDirectory;
    }

    public void Load()
    {
        if (!Directory.Exists(_suggestionsDirectory))
            return;

        string[] jsonFiles;
        try
        {
            jsonFiles = Directory.GetFiles(_suggestionsDirectory, "*.json");
        }
        catch
        {
            return;
        }

        foreach (string file in jsonFiles)
        {
            try
            {
                string json = File.ReadAllText(file);
                var entry = ParseSuggestionFile(json);
                if (entry != null && !string.IsNullOrEmpty(entry.Command))
                    _entries[entry.Command] = entry;
            }
            catch
            {
                continue;
            }
        }
    }

    public List<string> GetSubcommands(string command)
    {
        if (_entries.TryGetValue(command, out var entry))
            return entry.Subcommands;
        return new List<string>();
    }

    public List<string> GetFlags(string command)
    {
        if (_entries.TryGetValue(command, out var entry))
            return entry.Flags;
        return new List<string>();
    }

    public List<string> GetCompletionsForArg(string command, string partial)
    {
        if (!_entries.TryGetValue(command, out var entry))
            return new List<string>();

        var results = new List<string>();

        if (partial.StartsWith("-"))
        {
            foreach (string flag in entry.Flags)
            {
                if (flag.StartsWith(partial, StringComparison.OrdinalIgnoreCase))
                    results.Add(flag);
            }
        }
        else
        {
            foreach (string sub in entry.Subcommands)
            {
                if (sub.StartsWith(partial, StringComparison.OrdinalIgnoreCase))
                    results.Add(sub);
            }
        }

        results.Sort(StringComparer.OrdinalIgnoreCase);
        return results;
    }

    public bool HasCommand(string command)
    {
        return _entries.ContainsKey(command);
    }

    public void GenerateDefaults()
    {
        if (!Directory.Exists(_suggestionsDirectory))
        {
            try { Directory.CreateDirectory(_suggestionsDirectory); }
            catch { return; }
        }

        var defaults = BuildDefaultSuggestions();
        foreach (var entry in defaults)
        {
            string filePath = Path.Combine(_suggestionsDirectory, entry.Command + ".json");
            if (File.Exists(filePath))
                continue;

            try
            {
                string json = SerializeSuggestionEntry(entry);
                File.WriteAllText(filePath, json, Encoding.UTF8);
            }
            catch
            {
                continue;
            }
        }
    }

    private static SuggestionEntry? ParseSuggestionFile(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var entry = new SuggestionEntry();

            if (root.TryGetProperty("command", out var cmdProp))
                entry.Command = cmdProp.GetString() ?? "";

            if (root.TryGetProperty("subcommands", out var subProp) && subProp.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in subProp.EnumerateArray())
                {
                    string? val = item.GetString();
                    if (!string.IsNullOrEmpty(val))
                        entry.Subcommands.Add(val);
                }
            }

            if (root.TryGetProperty("flags", out var flagProp) && flagProp.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in flagProp.EnumerateArray())
                {
                    string? val = item.GetString();
                    if (!string.IsNullOrEmpty(val))
                        entry.Flags.Add(val);
                }
            }

            return entry;
        }
        catch
        {
            return null;
        }
    }

    private static string SerializeSuggestionEntry(SuggestionEntry entry)
    {
        var sb = new StringBuilder();
        sb.AppendLine("{");
        sb.Append("  \"command\": \"");
        sb.Append(JsonEscape(entry.Command));
        sb.AppendLine("\",");

        sb.AppendLine("  \"subcommands\": [");
        for (int i = 0; i < entry.Subcommands.Count; i++)
        {
            sb.Append("    \"");
            sb.Append(JsonEscape(entry.Subcommands[i]));
            sb.Append('"');
            if (i < entry.Subcommands.Count - 1)
                sb.Append(',');
            sb.AppendLine();
        }
        sb.AppendLine("  ],");

        sb.AppendLine("  \"flags\": [");
        for (int i = 0; i < entry.Flags.Count; i++)
        {
            sb.Append("    \"");
            sb.Append(JsonEscape(entry.Flags[i]));
            sb.Append('"');
            if (i < entry.Flags.Count - 1)
                sb.Append(',');
            sb.AppendLine();
        }
        sb.AppendLine("  ]");

        sb.Append('}');
        return sb.ToString();
    }

    private static string JsonEscape(string value)
    {
        var sb = new StringBuilder(value.Length);
        foreach (char c in value)
        {
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default: sb.Append(c); break;
            }
        }
        return sb.ToString();
    }

    private static List<SuggestionEntry> BuildDefaultSuggestions()
    {
        return new List<SuggestionEntry>
        {
            new SuggestionEntry
            {
                Command = "git",
                Subcommands = new List<string>
                {
                    "add", "bisect", "blame", "branch", "checkout", "cherry-pick",
                    "clean", "clone", "commit", "config", "describe", "diff",
                    "fetch", "format-patch", "gc", "grep", "init", "log",
                    "merge", "mv", "notes", "pull", "push", "rebase",
                    "reflog", "remote", "reset", "restore", "revert", "rm",
                    "shortlog", "show", "stash", "status", "submodule", "switch",
                    "tag", "worktree"
                },
                Flags = new List<string>
                {
                    "--help", "--version", "--verbose", "--quiet",
                    "--no-pager", "--git-dir", "--work-tree"
                }
            },
            new SuggestionEntry
            {
                Command = "docker",
                Subcommands = new List<string>
                {
                    "attach", "build", "commit", "compose", "container", "cp",
                    "create", "diff", "events", "exec", "export", "history",
                    "image", "images", "import", "info", "inspect", "kill",
                    "load", "login", "logout", "logs", "network", "node",
                    "pause", "port", "ps", "pull", "push", "rename",
                    "restart", "rm", "rmi", "run", "save", "search",
                    "service", "start", "stats", "stop", "system", "tag",
                    "top", "unpause", "update", "version", "volume", "wait"
                },
                Flags = new List<string>
                {
                    "--help", "--version", "--debug", "--host",
                    "--log-level", "--tls", "--tlsverify"
                }
            },
            new SuggestionEntry
            {
                Command = "npm",
                Subcommands = new List<string>
                {
                    "access", "adduser", "audit", "bugs", "cache", "ci",
                    "completion", "config", "dedupe", "deprecate", "diff",
                    "dist-tag", "docs", "doctor", "edit", "exec", "explain",
                    "explore", "find-dupes", "fund", "help", "init", "install",
                    "link", "list", "login", "logout", "ls", "outdated",
                    "owner", "pack", "ping", "pkg", "prefix", "profile",
                    "prune", "publish", "rebuild", "repo", "restart", "root",
                    "run", "search", "set", "shrinkwrap", "star", "stars",
                    "start", "stop", "test", "token", "uninstall", "unpublish",
                    "update", "version", "view", "whoami"
                },
                Flags = new List<string>
                {
                    "--help", "--version", "--global", "--save", "--save-dev",
                    "--save-exact", "--production", "--verbose", "--json",
                    "--long", "--parseable", "--registry"
                }
            },
            new SuggestionEntry
            {
                Command = "dotnet",
                Subcommands = new List<string>
                {
                    "add", "build", "build-server", "clean", "dev-certs",
                    "format", "help", "list", "migrate", "msbuild", "new",
                    "nuget", "pack", "publish", "remove", "restore", "run",
                    "sdk", "sln", "store", "test", "tool", "user-secrets",
                    "watch", "workload"
                },
                Flags = new List<string>
                {
                    "--help", "--version", "--info", "--list-runtimes",
                    "--list-sdks", "--diagnostics", "--verbose"
                }
            },
            new SuggestionEntry
            {
                Command = "pip",
                Subcommands = new List<string>
                {
                    "install", "download", "uninstall", "freeze", "inspect",
                    "list", "show", "check", "config", "search", "cache",
                    "index", "wheel", "hash", "completion", "debug", "help"
                },
                Flags = new List<string>
                {
                    "--help", "--version", "--verbose", "--quiet",
                    "--isolated", "--require-virtualenv", "--python",
                    "--proxy", "--retries", "--timeout", "--cache-dir",
                    "--no-cache-dir", "--disable-pip-version-check"
                }
            },
            new SuggestionEntry
            {
                Command = "cargo",
                Subcommands = new List<string>
                {
                    "add", "bench", "build", "check", "clean", "clippy",
                    "doc", "fetch", "fix", "fmt", "generate-lockfile",
                    "init", "install", "locate-project", "login", "logout",
                    "metadata", "new", "owner", "package", "pkgid", "publish",
                    "remove", "report", "run", "rustc", "rustdoc", "search",
                    "test", "tree", "uninstall", "update", "vendor", "verify-project",
                    "version", "yank"
                },
                Flags = new List<string>
                {
                    "--help", "--version", "--verbose", "--quiet",
                    "--color", "--frozen", "--locked", "--offline",
                    "--explain", "--jobs", "--keep-going"
                }
            },
            new SuggestionEntry
            {
                Command = "make",
                Subcommands = new List<string>
                {
                    "all", "clean", "install", "uninstall", "test",
                    "check", "dist", "distclean", "build", "run",
                    "help", "debug", "release"
                },
                Flags = new List<string>
                {
                    "--help", "--version", "--file", "--directory",
                    "--jobs", "--keep-going", "--silent", "--dry-run",
                    "--always-make", "--debug", "--environment-overrides",
                    "--ignore-errors", "--print-directory", "--question",
                    "--no-builtin-rules", "--touch", "--warn-undefined-variables"
                }
            },
            new SuggestionEntry
            {
                Command = "ssh",
                Subcommands = new List<string>(),
                Flags = new List<string>
                {
                    "-p", "-i", "-l", "-v", "-vv", "-vvv",
                    "-N", "-T", "-f", "-q", "-C", "-D", "-L", "-R",
                    "-o", "-F", "-4", "-6", "-A", "-X", "-Y"
                }
            },
            new SuggestionEntry
            {
                Command = "curl",
                Subcommands = new List<string>(),
                Flags = new List<string>
                {
                    "--help", "--version", "--verbose", "--silent", "--show-error",
                    "--output", "--location", "--data", "--header", "--request",
                    "--user", "--cookie", "--cookie-jar", "--form", "--insecure",
                    "--max-time", "--connect-timeout", "--retry", "--compressed",
                    "--user-agent", "--referer", "--proxy", "--cert", "--key",
                    "-X", "-H", "-d", "-o", "-O", "-L", "-s", "-S",
                    "-v", "-k", "-u", "-b", "-c", "-F", "-f", "-I"
                }
            },
            new SuggestionEntry
            {
                Command = "kubectl",
                Subcommands = new List<string>
                {
                    "annotate", "api-resources", "api-versions", "apply",
                    "attach", "auth", "autoscale", "certificate", "cluster-info",
                    "completion", "config", "cordon", "cp", "create", "debug",
                    "delete", "describe", "diff", "drain", "edit", "events",
                    "exec", "explain", "expose", "get", "kustomize", "label",
                    "logs", "patch", "plugin", "port-forward", "proxy",
                    "replace", "rollout", "run", "scale", "set", "taint",
                    "top", "uncordon", "version", "wait"
                },
                Flags = new List<string>
                {
                    "--help", "--namespace", "--context", "--cluster",
                    "--kubeconfig", "--output", "--selector", "--all-namespaces",
                    "-n", "-o", "-l", "-A", "--dry-run", "--field-selector"
                }
            },
            new SuggestionEntry
            {
                Command = "python",
                Subcommands = new List<string>(),
                Flags = new List<string>
                {
                    "--help", "--version", "-c", "-m", "-u", "-v",
                    "-V", "-W", "-x", "-b", "-B", "-d", "-E",
                    "-i", "-I", "-O", "-OO", "-q", "-s", "-S"
                }
            },
            new SuggestionEntry
            {
                Command = "python3",
                Subcommands = new List<string>(),
                Flags = new List<string>
                {
                    "--help", "--version", "-c", "-m", "-u", "-v",
                    "-V", "-W", "-x", "-b", "-B", "-d", "-E",
                    "-i", "-I", "-O", "-OO", "-q", "-s", "-S"
                }
            },
            new SuggestionEntry
            {
                Command = "node",
                Subcommands = new List<string>(),
                Flags = new List<string>
                {
                    "--help", "--version", "--eval", "--print", "--check",
                    "--interactive", "--inspect", "--inspect-brk",
                    "--require", "--input-type", "--experimental-modules",
                    "--no-warnings", "--trace-warnings",
                    "-e", "-p", "-c", "-i", "-r", "-v"
                }
            },
            new SuggestionEntry
            {
                Command = "apt",
                Subcommands = new List<string>
                {
                    "autoremove", "changelog", "clean", "depends",
                    "download", "edit-sources", "full-upgrade", "install",
                    "list", "moo", "policy", "purge", "rdepends",
                    "reinstall", "remove", "satisfy", "search", "show",
                    "showsrc", "source", "update", "upgrade"
                },
                Flags = new List<string>
                {
                    "--help", "--version", "--yes", "--assume-yes",
                    "--no-install-recommends", "--fix-broken",
                    "--simulate", "--dry-run", "--quiet",
                    "-y", "-f", "-s", "-q"
                }
            },
            new SuggestionEntry
            {
                Command = "brew",
                Subcommands = new List<string>
                {
                    "analytics", "autoremove", "cask", "cleanup",
                    "commands", "completions", "config", "deps",
                    "desc", "doctor", "fetch", "formulae", "help",
                    "home", "info", "install", "leaves", "link",
                    "list", "log", "migrate", "missing", "options",
                    "outdated", "pin", "postinstall", "readall",
                    "reinstall", "search", "services", "shellenv",
                    "tap", "tap-info", "uninstall", "unlink",
                    "unpin", "untap", "update", "upgrade", "uses"
                },
                Flags = new List<string>
                {
                    "--help", "--version", "--verbose", "--debug",
                    "--quiet", "--force", "--cask", "--formula",
                    "-v", "-d", "-q", "-f"
                }
            },
            new SuggestionEntry
            {
                Command = "pkg",
                Subcommands = new List<string>
                {
                    "autoclean", "clean", "files", "install",
                    "list-all", "list-installed", "reinstall",
                    "search", "show", "uninstall", "upgrade"
                },
                Flags = new List<string>
                {
                    "--help", "--version", "-y", "-f"
                }
            }
        };
    }
}
