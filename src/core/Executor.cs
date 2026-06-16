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

        if (input.StartsWith(';'))
        {
            return 1;
        }

        string trimmedInput = input.Trim();
        if (TryParseArithmeticOrAssignment(trimmedInput))
        {
            return 0;
        }

        if (IsAutoCdPath(trimmedInput))
        {
            string resolved = Utils.FileSystem.ResolvePath(trimmedInput, _workingDirectory);

            // Leading / or \ is an auto-cd trigger prefix (replaces ./)
            // If absolute resolution fails, strip the prefix and try relative to cwd
            if (!Directory.Exists(resolved) && trimmedInput.Length > 1 &&
                (trimmedInput[0] == '/' || trimmedInput[0] == '\\') &&
                !trimmedInput.StartsWith("~/") && !trimmedInput.StartsWith("~\\"))
            {
                string relative = trimmedInput.Substring(1);
                string relResolved = Utils.FileSystem.ResolvePath(relative, _workingDirectory);
                if (Directory.Exists(relResolved))
                {
                    resolved = relResolved;
                }
            }

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
                try
                {
                    string resolved = Utility.ResolveSubCommand(_env, _workingDirectory, tokens[i].Value);
                    string resolvedRaw = Utility.ResolveSubCommand(_env, _workingDirectory, tokens[i].RawExpandedValue);

                    if (ContextReader.isContext(resolved))
                    {
                        int colonIdx = resolved.IndexOf(':');
                        if (colonIdx > 0)
                        {
                            string contextName = resolved.Substring(0, colonIdx);
                            string attributeName = resolved.Substring(colonIdx + 1);
                            ContextReader contextReader = new ContextReader();
                            resolved = contextReader.GetAttributeValue(contextName, attributeName);
                            resolvedRaw = resolved; // Context replaces the whole token, so raw follows resolved
                        }
                    }
                    tokens[i] = new Token(TokenType.Word, resolved, tokens[i].Line, tokens[i].Column, tokens[i].WasQuoted, tokens[i].WasSingleQuoted, resolvedRaw);
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"aursh: subcommand resolution error: {ex.Message}");
                    return 1;
                }
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

        var linter = new AstLinter();
        var warnings = linter.Analyze(ast);
        foreach (var warning in warnings)
        {
            Console.Error.WriteLine($"aursh: warning: {warning.Message}");
        }

        var evaluator = new AstEvaluator(_env, this, _workingDirectory);
        return evaluator.Visit(ast);
    }

    public int ExecuteScript(string scriptContent)
    {
        if (string.IsNullOrWhiteSpace(scriptContent))
            return 0;

        Lexer lexer = new Lexer(scriptContent, _env);
        var tokens = lexer.Tokenize();
        Parser parser = new Parser(tokens);

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

        var linter = new AstLinter();
        var warnings = linter.Analyze(ast);
        foreach (var warning in warnings)
        {
            Console.Error.WriteLine($"aursh: warning: {warning.Message}");
        }

        var evaluator = new AstEvaluator(_env, this, _workingDirectory);
        return evaluator.Visit(ast);
    }

    public int ExecutePipeline(PipelineNode pipeline)
    {
        // PipelineNode execution should now go through AstEvaluator,
        // but Executor might still call this directly in older flows.
        var evaluator = new AstEvaluator(_env, this, _workingDirectory);
        return evaluator.Visit(pipeline);
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

    private bool IsAutoCdPath(string input)
    {
        if (string.IsNullOrEmpty(input))
        {
            return false;
        }

        if (input.Contains('|') || input.Contains(';') || input.Contains('>') ||
            input.Contains('<') || input.Contains('&'))
        {
            return false;
        }

        if (input == "." || input == "..")
        {
            return true;
        }

        if (input.StartsWith("./") || input.StartsWith(".\\"))
        {
            return false;
        }

        if (input.EndsWith("/") || input.EndsWith("\\") ||
            input.StartsWith("/") || input.StartsWith("\\") ||
            input.StartsWith("~/") || input.StartsWith("~\\") ||
            input.StartsWith("../") || input.StartsWith("..\\"))
        {
            try
            {
                string resolved = Utils.FileSystem.ResolvePath(input, _workingDirectory);
                if (Directory.Exists(resolved))
                {
                    return true;
                }
            }
            catch { }

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


    private bool TryParseArithmeticOrAssignment(string input)
    {
        var arrayInitMatch = System.Text.RegularExpressions.Regex.Match(input, @"^([a-zA-Z_][a-zA-Z0-9_]*)=\((.*)\)$");
        if (arrayInitMatch.Success)
        {
            string varName = arrayInitMatch.Groups[1].Value;
            string elements = arrayInitMatch.Groups[2].Value;
            var lexer = new Lexer(elements, _env);
            var tokens = lexer.Tokenize();
            var values = tokens.Where(t => t.Type == TokenType.Word).Select(t => t.Value).ToList();
            _env.SetArray(varName, values);
            return true;
        }

        var arrayAssignMatch = System.Text.RegularExpressions.Regex.Match(input, @"^([a-zA-Z_][a-zA-Z0-9_]*)\[(.*)\]=(.*)$");
        if (arrayAssignMatch.Success)
        {
            string varName = arrayAssignMatch.Groups[1].Value;
            string key = arrayAssignMatch.Groups[2].Value;
            string value = arrayAssignMatch.Groups[3].Value;

            var lexer = new Lexer(value, _env);
            var tokens = lexer.Tokenize();
            string expandedValue = string.Join("", tokens.Where(t => t.Type == TokenType.Word).Select(t => t.Value));

            if (int.TryParse(key, out int idx) && idx >= 0)
            {
                _env.SetArrayElement(varName, idx, expandedValue);
            }
            else
            {
                _env.SetAssocElement(varName, key, expandedValue);
            }
            return true;
        }

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
        return MathEvaluator.Evaluate(expression, _env);
    }
}
