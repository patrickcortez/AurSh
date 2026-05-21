using AurShell.Utils;
using System.Linq;

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
        if (TryParseArithmeticOrAssignment(trimmedInput))
        {
            return 0;
        }

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
                if (ContextReader.isContext(resolved))
                {
                    int colonIdx = resolved.IndexOf(':');
                    if (colonIdx > 0)
                    {
                        string contextName = resolved.Substring(0, colonIdx);
                        string attributeName = resolved.Substring(colonIdx + 1);
                        ContextReader contextReader = new ContextReader();
                        resolved = contextReader.GetAttributeValue(contextName, attributeName);
                    }
                }
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

    private bool TryParseArithmeticOrAssignment(string input)
    {
        var assignMatch = System.Text.RegularExpressions.Regex.Match(input, @"^([a-zA-Z_][a-zA-Z0-9_]*)=(.*)$");
        if (assignMatch.Success)
        {
            string varName = assignMatch.Groups[1].Value;
            string value = assignMatch.Groups[2].Value;
            
            var lexer = new Lexer(value, _env);
            var tokens = lexer.Tokenize();
            string expandedValue = string.Join("", tokens.Where(t => t.Type == TokenType.Word).Select(t => t.Value));
            
            _env.Set(varName, expandedValue);
            return true;
        }

        var arithMatch = System.Text.RegularExpressions.Regex.Match(input, @"^\$?([a-zA-Z_][a-zA-Z0-9_]*)\s*(\+=|-=|\*=|/=|)\s*(.+)$");
        if (arithMatch.Success && !string.IsNullOrEmpty(arithMatch.Groups[2].Value))
        {
            string varName = arithMatch.Groups[1].Value;
            string op = arithMatch.Groups[2].Value;
            string rightSide = arithMatch.Groups[3].Value;

            string currentValStr = _env.Get(varName) ?? "0";
            if (!double.TryParse(currentValStr, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double currentVal)) currentVal = 0;

            var rightLexer = new Lexer(rightSide, _env);
            string rightExpanded = string.Join("", rightLexer.Tokenize().Where(t => t.Type == TokenType.Word).Select(t => t.Value));
            
            double rightVal = EvaluateMath(rightExpanded);

            double result = currentVal;
            switch (op)
            {
                case "+=": result += rightVal; break;
                case "-=": result -= rightVal; break;
                case "*=": result *= rightVal; break;
                case "/=": if (rightVal != 0) result /= rightVal; break;
            }

            _env.Set(varName, result.ToString(System.Globalization.CultureInfo.InvariantCulture));
            return true;
        }

        var assignArithMatch = System.Text.RegularExpressions.Regex.Match(input, @"^\$?([a-zA-Z_][a-zA-Z0-9_]*)\s*=\s*(.+)$");
        if (assignArithMatch.Success)
        {
            string varName = assignArithMatch.Groups[1].Value;
            string rightSide = assignArithMatch.Groups[2].Value;
            
            var rightLexer = new Lexer(rightSide, _env);
            string rightExpanded = string.Join("", rightLexer.Tokenize().Where(t => t.Type == TokenType.Word).Select(t => t.Value));
            
            if (System.Text.RegularExpressions.Regex.IsMatch(rightExpanded, @"[\+\-\*/]"))
            {
                double result = EvaluateMath(rightExpanded);
                _env.Set(varName, result.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }
            else
            {
                _env.Set(varName, rightExpanded);
            }
            return true;
        }

        return false;
    }

    private double EvaluateMath(string expression)
    {
        expression = expression.Replace(" ", "");
        var tokens = System.Text.RegularExpressions.Regex.Split(expression, @"([\+\-\*/])").Where(s => !string.IsNullOrEmpty(s)).ToList();
        
        if (tokens.Count > 1 && (tokens[0] == "-" || tokens[0] == "+"))
        {
            tokens[1] = tokens[0] + tokens[1];
            tokens.RemoveAt(0);
        }

        if (tokens.Count == 0) return 0;
        if (!double.TryParse(tokens[0], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double result)) return 0;

        for (int i = 1; i < tokens.Count - 1; i += 2)
        {
            string op = tokens[i];
            if (!double.TryParse(tokens[i + 1], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double next)) next = 0;
            switch (op)
            {
                case "+": result += next; break;
                case "-": result -= next; break;
                case "*": result *= next; break;
                case "/": if (next != 0) result /= next; break;
            }
        }
        return result;
    }
}
