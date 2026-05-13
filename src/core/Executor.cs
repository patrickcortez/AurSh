using AurShell.Utils;

namespace AurShell.Core;

public class Executor
{
    private readonly ShellEnvironment _env;
    private string _workingDirectory;

    public string WorkingDirectory
    {
        get => _workingDirectory;
        set => _workingDirectory = value;
    }

    public Executor(ShellEnvironment env, string workingDirectory)
    {
        _env = env;
        _workingDirectory = workingDirectory;
    }

    public (int ExitCode, string Output) ExecuteCapture(string input)
    {
        string tempFile = System.IO.Path.GetTempFileName();
        try
        {
            int exit = Execute($"{input} > {tempFile}");
            string output = System.IO.File.ReadAllText(tempFile).TrimEnd('\n', '\r');
            return (exit, output);
        }
        finally
        {
            try { System.IO.File.Delete(tempFile); } catch { }
        }
    }

    public int Execute(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return 0;

        if(input.StartsWith(';')){
            return 1;
        }

        if (input.Contains('\n'))
        {
            return ExecuteMultilineAsNativeScript(input);
        }

        string trimmedInput = input.Trim();
        if (IsAutoCdPath(trimmedInput))
        {
            string resolved = Utils.FileSystem.ResolvePath(trimmedInput, _workingDirectory);
            if (Directory.Exists(resolved))
            {
                string oldDir = _workingDirectory;
                _workingDirectory = Utils.FileSystem.NormalizePath(resolved);
                try { Directory.SetCurrentDirectory(_workingDirectory); } catch { }
                _env.Set("OLDPWD", oldDir);
                _env.Set("PWD", _workingDirectory);
                return 0;
            }
            if (!File.Exists(resolved))
            {
                Console.Error.WriteLine($"aursh: cd: {trimmedInput}: No such file or directory");
                return 1;
            }
        }

        string expanded = ResolveAliases(input);

        var lexer = new Lexer(expanded, _env);
        List<Token> tokens;

        try
        {
            tokens = lexer.Tokenize();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"aursh: tokenization error: {ex.Message}");
            return 1;
        }

        if (tokens.Count <= 1)
            return 0;

        for (int i = 0; i < tokens.Count; i++)
        {
            if (tokens[i].Type == TokenType.Word && !tokens[i].WasSingleQuoted)
            {
                string resolved = Utility.ResolveSubCommand(_env, _workingDirectory, tokens[i].Value);
                tokens[i] = new Token(TokenType.Word, resolved, tokens[i].WasQuoted, tokens[i].WasSingleQuoted);
            }
        }

        var parser = new Parser(tokens);
        ListNode ast;

        try
        {
            ast = parser.Parse();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"aursh: parse error: {ex.Message}");
            return 1;
        }

        return ExecuteList(ast);
    }

    public int ExecuteList(ListNode list)
    {
        int lastExit = 0;

        for (int i = 0; i < list.Entries.Count; i++)
        {
            var entry = list.Entries[i];

            if (i > 0)
            {
                var prevOp = list.Entries[i - 1].Operator;

                if (prevOp == ListOperator.And && lastExit != 0)
                    continue;

                if (prevOp == ListOperator.Or && lastExit == 0)
                    continue;
            }

            lastExit = ExecutePipeline(entry.Pipeline);
            _env.LastExitCode = lastExit;
        }

        return lastExit;
    }

    public int ExecutePipeline(PipelineNode pipeline)
    {
        if (pipeline.Commands.Count == 0)
            return 0;

        try
        {
            return Pipeline.Execute(pipeline, _env, _workingDirectory);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"aursh: execution error: {ex.Message}");
            return 1;
        }
        finally
        {
            SyncWorkingDirectory();
        }
    }

    private string ResolveAliases(string input)
    {
        string trimmed = input.TrimStart();
        int spaceIdx = trimmed.IndexOf(' ');
        string firstWord = spaceIdx >= 0 ? trimmed.Substring(0, spaceIdx) : trimmed;
        string remainder = spaceIdx >= 0 ? trimmed.Substring(spaceIdx) : "";

        int depth = 0;
        while (depth < 10)
        {
            string? alias = _env.GetAlias(firstWord);
            if (alias == null)
                break;

            string expandedTrimmed = alias.TrimStart();
            int nextSpace = expandedTrimmed.IndexOf(' ');
            string nextFirst = nextSpace >= 0 ? expandedTrimmed.Substring(0, nextSpace) : expandedTrimmed;

            if (nextFirst == firstWord)
                break;

            firstWord = nextFirst;
            string aliasRemainder = nextSpace >= 0 ? expandedTrimmed.Substring(nextSpace) : "";
            remainder = aliasRemainder + remainder;
            depth++;
        }

        return firstWord + remainder;
    }

    private void SyncWorkingDirectory()
    {
        try
        {
            string envCwd = _env.Get("PWD") ?? _workingDirectory;
            if (Directory.Exists(envCwd))
                _workingDirectory = envCwd;
        }
        catch { }
    }

    private static bool IsAutoCdPath(string input)
    {
        if (string.IsNullOrEmpty(input))
            return false;

        if (input.Contains('|') || input.Contains(';') || input.Contains('>') ||
            input.Contains('<') || input.Contains('&'))
            return false;

        if (input == "." || input == "..")
            return true;

        if (input.StartsWith("./") || input.StartsWith(".\\") ||
            input.StartsWith("../") || input.StartsWith("..\\"))
        {
            bool hasSpaces = false;
            for (int i = 0; i < input.Length; i++)
            {
                if (input[i] == ' ')
                {
                    hasSpaces = true;
                    break;
                }
            }
            return !hasSpaces;
        }

        return false;
    }

    private int ExecuteMultilineAsNativeScript(string input)
    {
        string ext = Utils.Platform.CurrentOS == Utils.OperatingSystemType.Windows ? ".ps1" : ".sh";
        string tempFile = Path.Combine(Path.GetTempPath(), $"aursh_script_{Guid.NewGuid()}{ext}");
        
        try
        {
            File.WriteAllText(tempFile, input);
            
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = Utils.Platform.CurrentOS == Utils.OperatingSystemType.Windows ? "powershell.exe" : Utils.Platform.DefaultShell,
                UseShellExecute = false,
                CreateNoWindow = false,
                WorkingDirectory = _workingDirectory
            };

            if (Utils.Platform.CurrentOS == Utils.OperatingSystemType.Windows)
            {
                psi.ArgumentList.Add("-NoProfile");
                psi.ArgumentList.Add("-ExecutionPolicy");
                psi.ArgumentList.Add("Bypass");
                psi.ArgumentList.Add("-File");
                psi.ArgumentList.Add(tempFile);
            }
            else
            {
                psi.ArgumentList.Add(tempFile);
            }

            foreach (var kv in _env.Variables)
                psi.Environment[kv.Key] = kv.Value;

            using var process = System.Diagnostics.Process.Start(psi);
            if (process != null)
            {
                process.WaitForExit();
                _env.LastExitCode = process.ExitCode;
                return process.ExitCode;
            }
            return 127;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"aursh: multiline execution failed: {ex.Message}");
            return 126;
        }
        finally
        {
            try { File.Delete(tempFile); } catch { }
            SyncWorkingDirectory();
        }
    }
}
