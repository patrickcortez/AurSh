using System.Text;

namespace AurShell.Core;

public static class BuiltinCommands
{
    private static readonly HashSet<string> Builtins = new(StringComparer.OrdinalIgnoreCase)
    {
        "cd", "export", "unset", "exit", "history", "clear", "echo",
        "pwd", "type", "alias", "unalias", "source", "set", "env",
        "true", "false", "shift", "read", "test", "return"
    };

    public static bool IsBuiltin(string name) => Builtins.Contains(name);

    public static int Execute(CommandNode cmd, ShellEnvironment env, ref string workingDirectory)
    {
        return cmd.Name.ToLowerInvariant() switch
        {
            "cd" => ExecuteCd(cmd, env, ref workingDirectory),
            "export" => ExecuteExport(cmd, env),
            "unset" => ExecuteUnset(cmd, env),
            "exit" => ExecuteExit(cmd),
            "history" => ExecuteHistory(cmd),
            "clear" => ExecuteClear(),
            "echo" => ExecuteEcho(cmd),
            "pwd" => ExecutePwd(workingDirectory),
            "type" => ExecuteType(cmd, env, workingDirectory),
            "alias" => ExecuteAlias(cmd, env),
            "unalias" => ExecuteUnalias(cmd, env),
            "source" => ExecuteSource(cmd, env, ref workingDirectory),
            "set" => ExecuteSet(cmd, env),
            "env" => ExecuteEnv(env),
            "true" => 0,
            "false" => 1,
            "read" => ExecuteRead(cmd, env),
            "test" => ExecuteTest(cmd),
            "return" => ExecuteReturn(cmd, env),
            _ => ExecuteFallback(cmd)
        };
    }

    private static int ExecuteCd(CommandNode cmd, ShellEnvironment env, ref string workingDirectory)
    {
        string target;

        if (cmd.Args.Count == 0)
        {
            target = Utils.Platform.HomeDirectory;
        }
        else if (cmd.Args[0] == "-")
        {
            string? oldPwd = env.Get("OLDPWD");
            if (string.IsNullOrEmpty(oldPwd))
            {
                Console.Error.WriteLine("aursh: cd: OLDPWD not set");
                return 1;
            }
            target = oldPwd;
            Console.WriteLine(target);
        }
        else
        {
            target = Utils.FileSystem.ResolvePath(cmd.Args[0], workingDirectory);
        }

        if (!Directory.Exists(target))
        {
            Console.Error.WriteLine($"aursh: cd: {cmd.Args.FirstOrDefault() ?? target}: No such file or directory");
            return 1;
        }

        string oldDir = workingDirectory;
        workingDirectory = Utils.FileSystem.NormalizePath(target);

        try
        {
            Directory.SetCurrentDirectory(workingDirectory);
        }
        catch { }

        env.Set("OLDPWD", oldDir);
        env.Set("PWD", workingDirectory);

        return 0;
    }

    private static int ExecuteExport(CommandNode cmd, ShellEnvironment env)
    {
        if (cmd.Args.Count == 0)
        {
            foreach (var kv in env.Variables.OrderBy(k => k.Key))
                Console.WriteLine($"export {kv.Key}=\"{kv.Value}\"");
            return 0;
        }

        if (cmd.Args.Count >= 1 && cmd.Args[0] == "-obj")
        {
            if (cmd.Args.Count < 3)
            {
                Console.Error.WriteLine("aursh: export -obj: usage: export -obj NAME {key:value, ...}");
                return 1;
            }

            string objName = cmd.Args[1];
            string rest = string.Join(" ", cmd.Args.Skip(2));
            var obj = env.ParseObjectLiteral(rest);

            if (obj == null)
            {
                Console.Error.WriteLine($"aursh: export -obj: invalid object syntax");
                return 1;
            }

            env.SetObject(objName, obj);
            env.ExportToSystem(objName);
            return 0;
        }

        if (cmd.Args.Count >= 1 && cmd.Args[0] == "-p")
        {
            foreach (var kv in env.Variables.OrderBy(k => k.Key))
                Console.WriteLine($"declare -x {kv.Key}=\"{kv.Value}\"");
            return 0;
        }

        foreach (string arg in cmd.Args)
        {
            int eqIdx = arg.IndexOf('=');
            if (eqIdx >= 0)
            {
                string name = arg.Substring(0, eqIdx);
                string value = arg.Substring(eqIdx + 1);

                if ((value.StartsWith('"') && value.EndsWith('"')) ||
                    (value.StartsWith('\'') && value.EndsWith('\'')))
                    value = value.Substring(1, value.Length - 2);

                env.Set(name, value);
                env.ExportToSystem(name);
            }
            else
            {
                env.ExportToSystem(arg);
            }
        }

        return 0;
    }

    private static int ExecuteUnset(CommandNode cmd, ShellEnvironment env)
    {
        if (cmd.Args.Count == 0)
        {
            Console.Error.WriteLine("aursh: unset: not enough arguments");
            return 1;
        }

        foreach (string name in cmd.Args)
        {
            env.Unset(name);
            System.Environment.SetEnvironmentVariable(name, null);
        }

        return 0;
    }

    private static int ExecuteExit(CommandNode cmd)
    {
        int code = 0;
        if (cmd.Args.Count > 0)
            int.TryParse(cmd.Args[0], out code);

        System.Environment.Exit(code);
        return code;
    }

    private static int ExecuteHistory(CommandNode cmd)
    {
        string historyFile = Utils.Platform.HistoryFilePath;
        string[] lines = Utils.FileSystem.ReadAllLinesSafe(historyFile);

        if (cmd.Args.Count > 0 && cmd.Args[0] == "-c")
        {
            Utils.FileSystem.WriteAllTextSafe(historyFile, "");
            return 0;
        }

        int start = 0;
        if (cmd.Args.Count > 0 && int.TryParse(cmd.Args[0], out int count))
            start = Math.Max(0, lines.Length - count);

        for (int i = start; i < lines.Length; i++)
            Console.WriteLine($"  {i + 1}  {lines[i]}");

        return 0;
    }

    private static int ExecuteClear()
    {
        Console.Write(Utils.Ansi.ClearScreen);
        Console.Write(Utils.Ansi.SetCursorPosition(1, 1));
        return 0;
    }

    private static int ExecuteEcho(CommandNode cmd)
    {
        bool noNewline = false;
        bool interpretEscapes = false;
        int startIdx = 0;

        while (startIdx < cmd.Args.Count)
        {
            string arg = cmd.Args[startIdx];
            if (arg == "-n")
            {
                noNewline = true;
                startIdx++;
            }
            else if (arg == "-e")
            {
                interpretEscapes = true;
                startIdx++;
            }
            else if (arg == "-E")
            {
                interpretEscapes = false;
                startIdx++;
            }
            else
            {
                break;
            }
        }

        string output = string.Join(" ", cmd.Args.Skip(startIdx));

        if (interpretEscapes)
            output = InterpretEscapes(output);

        if (noNewline)
            Console.Write(output);
        else
            Console.WriteLine(output);

        return 0;
    }

    private static int ExecutePwd(string workingDirectory)
    {
        Console.WriteLine(workingDirectory);
        return 0;
    }

    private static int ExecuteType(CommandNode cmd, ShellEnvironment env, string workingDirectory)
    {
        if (cmd.Args.Count == 0)
        {
            Console.Error.WriteLine("aursh: type: not enough arguments");
            return 1;
        }

        int result = 0;

        foreach (string name in cmd.Args)
        {
            if (IsBuiltin(name))
            {
                Console.WriteLine($"{name} is a shell builtin");
            }
            else if (env.GetAlias(name) != null)
            {
                Console.WriteLine($"{name} is aliased to '{env.GetAlias(name)}'");
            }
            else
            {
                string? path = Pipeline.ResolveCommand(name, workingDirectory);
                if (path != null)
                {
                    Console.WriteLine($"{name} is {path}");
                }
                else
                {
                    Console.Error.WriteLine($"aursh: type: {name}: not found");
                    result = 1;
                }
            }
        }

        return result;
    }

    private static int ExecuteAlias(CommandNode cmd, ShellEnvironment env)
    {
        if (cmd.Args.Count == 0)
        {
            foreach (var kv in env.Aliases.OrderBy(k => k.Key))
                Console.WriteLine($"alias {kv.Key}='{kv.Value}'");
            return 0;
        }

        foreach (string arg in cmd.Args)
        {
            int eqIdx = arg.IndexOf('=');
            if (eqIdx >= 0)
            {
                string name = arg.Substring(0, eqIdx);
                string value = arg.Substring(eqIdx + 1);

                if ((value.StartsWith('\'') && value.EndsWith('\'')) ||
                    (value.StartsWith('"') && value.EndsWith('"')))
                    value = value.Substring(1, value.Length - 2);

                env.SetAlias(name, value);
            }
            else
            {
                string? alias = env.GetAlias(arg);
                if (alias != null)
                    Console.WriteLine($"alias {arg}='{alias}'");
                else
                {
                    Console.Error.WriteLine($"aursh: alias: {arg}: not found");
                    return 1;
                }
            }
        }

        return 0;
    }

    private static int ExecuteUnalias(CommandNode cmd, ShellEnvironment env)
    {
        if (cmd.Args.Count == 0)
        {
            Console.Error.WriteLine("aursh: unalias: not enough arguments");
            return 1;
        }

        if (cmd.Args[0] == "-a")
        {
            foreach (string key in env.Aliases.Keys.ToList())
                env.UnsetAlias(key);
            return 0;
        }

        foreach (string name in cmd.Args)
        {
            if (!env.UnsetAlias(name))
            {
                Console.Error.WriteLine($"aursh: unalias: {name}: not found");
                return 1;
            }
        }

        return 0;
    }

    private static int ExecuteSource(CommandNode cmd, ShellEnvironment env, ref string workingDirectory)
    {
        if (cmd.Args.Count == 0)
        {
            Console.Error.WriteLine("aursh: source: filename argument required");
            return 1;
        }

        string filePath = Utils.FileSystem.ResolvePath(cmd.Args[0], workingDirectory);

        if (!File.Exists(filePath))
        {
            Console.Error.WriteLine($"aursh: source: {cmd.Args[0]}: No such file or directory");
            return 1;
        }

        string extension = Path.GetExtension(filePath).ToLowerInvariant();
        string[] scriptArgs = cmd.Args.Count > 1 ? cmd.Args.Skip(1).ToArray() : Array.Empty<string>();

        if (extension == ".aur")
        {
            var runner = new ScriptRunner(env, workingDirectory);
            int result = runner.RunFile(filePath, scriptArgs);
            return result;
        }

        var executor = new Executor(env, workingDirectory);
        var rcLoader = new RcLoader(env, executor);
        int rcResult = rcLoader.LoadFrom(filePath);
        workingDirectory = executor.WorkingDirectory;
        return rcResult;
    }

    private static int ExecuteSet(CommandNode cmd, ShellEnvironment env)
    {
        if (cmd.Args.Count == 0)
        {
            foreach (var kv in env.Variables.OrderBy(k => k.Key))
                Console.WriteLine($"{kv.Key}={kv.Value}");
            return 0;
        }

        for (int i = 0; i < cmd.Args.Count; i++)
        {
            string arg = cmd.Args[i];
            int eqIdx = arg.IndexOf('=');
            if (eqIdx >= 0)
            {
                string name = arg.Substring(0, eqIdx);
                string value = arg.Substring(eqIdx + 1);
                env.Set(name, value);
            }
        }

        return 0;
    }

    private static int ExecuteEnv(ShellEnvironment env)
    {
        foreach (var kv in env.Variables.OrderBy(k => k.Key))
            Console.WriteLine($"{kv.Key}={kv.Value}");
        return 0;
    }

    private static int ExecuteRead(CommandNode cmd, ShellEnvironment env)
    {
        string varName = cmd.Args.Count > 0 ? cmd.Args[0] : "REPLY";
        string prompt = "";

        for (int i = 0; i < cmd.Args.Count - 1; i++)
        {
            if (cmd.Args[i] == "-p" && i + 1 < cmd.Args.Count)
            {
                prompt = cmd.Args[i + 1];
                varName = cmd.Args.Count > i + 2 ? cmd.Args[i + 2] : "REPLY";
                break;
            }
        }

        if (!string.IsNullOrEmpty(prompt))
            Console.Write(prompt);

        string? line = Console.ReadLine();
        env.Set(varName, line ?? "");

        return line == null ? 1 : 0;
    }

    private static int ExecuteTest(CommandNode cmd)
    {
        if (cmd.Args.Count == 0)
            return 1;

        if (cmd.Args.Count == 1)
            return string.IsNullOrEmpty(cmd.Args[0]) ? 1 : 0;

        if (cmd.Args.Count == 2)
        {
            string op = cmd.Args[0];
            string operand = cmd.Args[1];

            return op switch
            {
                "-z" => string.IsNullOrEmpty(operand) ? 0 : 1,
                "-n" => !string.IsNullOrEmpty(operand) ? 0 : 1,
                "-f" => File.Exists(operand) ? 0 : 1,
                "-d" => Directory.Exists(operand) ? 0 : 1,
                "-e" => (File.Exists(operand) || Directory.Exists(operand)) ? 0 : 1,
                "-r" => File.Exists(operand) ? 0 : 1,
                "-w" => File.Exists(operand) ? 0 : 1,
                "-x" => File.Exists(operand) ? 0 : 1,
                "-s" => (File.Exists(operand) && new FileInfo(operand).Length > 0) ? 0 : 1,
                "!" => string.IsNullOrEmpty(operand) ? 0 : 1,
                _ => 1
            };
        }

        if (cmd.Args.Count == 3)
        {
            string left = cmd.Args[0];
            string op = cmd.Args[1];
            string right = cmd.Args[2];

            return op switch
            {
                "=" or "==" => left == right ? 0 : 1,
                "!=" => left != right ? 0 : 1,
                "-eq" => ParseIntCompare(left, right, (a, b) => a == b),
                "-ne" => ParseIntCompare(left, right, (a, b) => a != b),
                "-lt" => ParseIntCompare(left, right, (a, b) => a < b),
                "-le" => ParseIntCompare(left, right, (a, b) => a <= b),
                "-gt" => ParseIntCompare(left, right, (a, b) => a > b),
                "-ge" => ParseIntCompare(left, right, (a, b) => a >= b),
                _ => 1
            };
        }

        return 1;
    }

    private static int ExecuteReturn(CommandNode cmd, ShellEnvironment env)
    {
        int code = 0;
        if (cmd.Args.Count > 0)
            int.TryParse(cmd.Args[0], out code);
        env.LastExitCode = code;
        return code;
    }

    private static int ExecuteFallback(CommandNode cmd)
    {
        Console.Error.WriteLine($"aursh: {cmd.Name}: builtin not implemented");
        return 1;
    }

    private static int ParseIntCompare(string left, string right, Func<int, int, bool> predicate)
    {
        if (int.TryParse(left, out int l) && int.TryParse(right, out int r))
            return predicate(l, r) ? 0 : 1;
        return 2;
    }

    private static string InterpretEscapes(string input)
    {
        var sb = new StringBuilder(input.Length);
        for (int i = 0; i < input.Length; i++)
        {
            if (input[i] == '\\' && i + 1 < input.Length)
            {
                char next = input[i + 1];
                switch (next)
                {
                    case 'n': sb.Append('\n'); i++; break;
                    case 't': sb.Append('\t'); i++; break;
                    case 'r': sb.Append('\r'); i++; break;
                    case 'a': sb.Append('\a'); i++; break;
                    case 'b': sb.Append('\b'); i++; break;
                    case '\\': sb.Append('\\'); i++; break;
                    case '0': sb.Append('\0'); i++; break;
                    case 'e': sb.Append('\x1b'); i++; break;
                    default: sb.Append('\\'); sb.Append(next); i++; break;
                }
            }
            else
            {
                sb.Append(input[i]);
            }
        }
        return sb.ToString();
    }
}
