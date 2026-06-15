using System.Text;

namespace AurShell.Core;

public class ScriptRunner
{
    private readonly ShellEnvironment _env;
    private readonly Executor _executor;
    private readonly Dictionary<string, FunctionDef> _functions = new(StringComparer.Ordinal);
    private string[] _scriptArgs = Array.Empty<string>();
    private string _scriptName = "";
    private bool _returnRequested;
    private int _returnCode;
    private bool _breakRequested;
    private int _breakDepth;
    private bool _continueRequested;
    private int _continueDepth;

    private class FunctionDef
    {
        public string Name { get; set; } = "";
        public List<string> Body { get; set; } = new();
    }

    public ScriptRunner(ShellEnvironment env, string workingDirectory)
    {
        _env = env;
        _executor = new Executor(env, workingDirectory);
    }

    public ScriptRunner(ShellEnvironment env, Executor executor)
    {
        _env = env;
        _executor = executor;
    }

    public int RunFile(string filePath, string[] args)
    {
        if (!File.Exists(filePath))
        {
            Console.Error.WriteLine($"aursh: {filePath}: No such file or directory");
            return 127;
        }

        string content = File.ReadAllText(filePath);
        _scriptName = filePath;
        _scriptArgs = args;

        SetPositionalParams(args);

        return RunScript(content);
    }

    public int RunString(string script)
    {
        return RunScript(script);
    }

    public int RunScript(string content)
    {
        List<string> lines = SplitLines(content);
        int index = 0;
        int lastResult = 0;

        PreScanFunctions(lines);

        while (index < lines.Count)
        {
            if (_returnRequested)
            {
                _returnRequested = false;
                return _returnCode;
            }

            lastResult = ExecuteLine(lines, ref index);
            _env.LastExitCode = lastResult;
        }

        return lastResult;
    }

    private int ExecuteLine(List<string> lines, ref int index)
    {
        if (index >= lines.Count)
            return 0;

        string line = lines[index].Trim();

        if (string.IsNullOrEmpty(line) || line.StartsWith('#'))
        {
            index++;
            return 0;
        }

        if (line.StartsWith("if ") || line == "if")
            return ExecuteIf(lines, ref index);

        if (line.StartsWith("case ") || line == "case")
            return ExecuteCase(lines, ref index);

        if (line.StartsWith("for ") || line == "for")
            return ExecuteFor(lines, ref index);

        if (line.StartsWith("while ") || line == "while")
            return ExecuteWhile(lines, ref index);

        if (line.StartsWith("until ") || line == "until")
            return ExecuteUntil(lines, ref index);

        if (line.StartsWith("function ") || line.Contains("()"))
        {
            if (IsFunctionDefinition(line))
                return SkipFunctionDefinition(lines, ref index);
        }

        if (line == "return" || line.StartsWith("return "))
        {
            _returnCode = 0;
            if (line.StartsWith("return "))
            {
                string codeStr = line.Substring(7).Trim();
                int.TryParse(codeStr, out _returnCode);
            }
            _returnRequested = true;
            index++;
            return _returnCode;
        }

        if (line == "break" || line.StartsWith("break "))
        {
            _breakDepth = 1;
            if (line.StartsWith("break "))
            {
                string depthStr = line.Substring(6).Trim();
                if (int.TryParse(depthStr, out int d) && d > 0)
                {
                    _breakDepth = d;
                }
            }
            _breakRequested = true;
            index++;
            return 0;
        }

        if (line == "continue" || line.StartsWith("continue "))
        {
            _continueDepth = 1;
            if (line.StartsWith("continue "))
            {
                string depthStr = line.Substring(9).Trim();
                if (int.TryParse(depthStr, out int d) && d > 0)
                {
                    _continueDepth = d;
                }
            }
            _continueRequested = true;
            index++;
            return 0;
        }

        if (line == "shift" || line.StartsWith("shift "))
        {
            int n = 1;
            if (line.StartsWith("shift "))
            {
                int.TryParse(line.Substring(6).Trim(), out n);
            }
            if (n > 0)
            {
                if (n <= _scriptArgs.Length)
                {
                    _scriptArgs = _scriptArgs.Skip(n).ToArray();
                }
                else
                {
                    _scriptArgs = Array.Empty<string>();
                }
                SetPositionalParams(_scriptArgs);
            }
            index++;
            return 0;
        }

        string expanded = ExpandPositionalParams(line);

        if (_functions.TryGetValue(GetCommandName(expanded), out var func))
        {
            index++;
            return ExecuteFunction(func, expanded);
        }

        index++;
        return _executor.Execute(expanded);
    }

    private int ExecuteIf(List<string> lines, ref int index)
    {
        string line = lines[index].Trim();
        index++;

        string condition = ExtractCondition(line, "if");
        bool conditionMet = EvaluateCondition(condition);
        bool anyBranchExecuted = false;

        List<string> thenBlock = new();
        List<string> elseBlock = new();
        List<(string condition, List<string> block)> elifBlocks = new();
        bool inElse = false;
        string? currentElifCondition = null;
        List<string>? currentElifBlock = null;
        int depth = 0;

        while (index < lines.Count)
        {
            string current = lines[index].Trim();

            if (current.StartsWith("if ") || current == "if")
                depth++;

            if (current == "fi")
            {
                if (depth == 0)
                {
                    if (currentElifCondition != null && currentElifBlock != null)
                        elifBlocks.Add((currentElifCondition, currentElifBlock));

                    index++;
                    break;
                }
                depth--;
            }

            if (depth == 0)
            {
                if (current.StartsWith("elif ") && !inElse)
                {
                    if (currentElifCondition != null && currentElifBlock != null)
                        elifBlocks.Add((currentElifCondition, currentElifBlock));

                    currentElifCondition = ExtractCondition(current, "elif");
                    currentElifBlock = new List<string>();
                    index++;
                    continue;
                }

                if (current == "else")
                {
                    if (currentElifCondition != null && currentElifBlock != null)
                    {
                        elifBlocks.Add((currentElifCondition, currentElifBlock));
                        currentElifCondition = null;
                        currentElifBlock = null;
                    }
                    inElse = true;
                    index++;
                    continue;
                }

                if (current == "then")
                {
                    index++;
                    continue;
                }
                if (current.StartsWith("then "))
                {
                    lines[index] = current.Substring(5).Trim();
                    current = lines[index];
                }
            }

            if (currentElifBlock != null)
                currentElifBlock.Add(lines[index]);
            else if (inElse)
                elseBlock.Add(lines[index]);
            else
                thenBlock.Add(lines[index]);

            index++;
        }

        int result = 0;

        if (conditionMet)
        {
            result = ExecuteBlock(thenBlock);
            anyBranchExecuted = true;
        }

        if (!anyBranchExecuted)
        {
            foreach (var (elifCond, elifBlock) in elifBlocks)
            {
                if (EvaluateCondition(elifCond))
                {
                    result = ExecuteBlock(elifBlock);
                    anyBranchExecuted = true;
                    break;
                }
            }
        }

        if (!anyBranchExecuted && elseBlock.Count > 0)
        {
            result = ExecuteBlock(elseBlock);
        }

        return result;
    }

    private int ExecuteFor(List<string> lines, ref int index)
    {
        string line = lines[index].Trim();
        index++;

        string varName = "";
        List<string> iterValues = new();

        string afterFor = line.Substring(3).Trim();
        int inIdx = afterFor.IndexOf(" in ", StringComparison.Ordinal);
        if (inIdx >= 0)
        {
            varName = afterFor.Substring(0, inIdx).Trim();
            string valuesPart = afterFor.Substring(inIdx + 4).Trim();

            if (valuesPart.EndsWith(";"))
                valuesPart = valuesPart.Substring(0, valuesPart.Length - 1).Trim();

            iterValues = SplitValues(ExpandPositionalParams(valuesPart));
        }

        List<string> body = new();
        int depth = 0;
        bool passedDo = false;

        while (index < lines.Count)
        {
            string current = lines[index].Trim();

            if (!passedDo)
            {
                if (current == "do" || current.StartsWith("do;"))
                {
                    passedDo = true;
                    index++;
                    continue;
                }
                if (current.StartsWith("do "))
                {
                    passedDo = true;
                    lines[index] = current.Substring(3).Trim();
                    current = lines[index];
                }
            }

            if (current.StartsWith("for ") || current.StartsWith("while ") || current.StartsWith("until "))
                depth++;

            if (current == "done")
            {
                if (depth == 0)
                {
                    index++;
                    break;
                }
                depth--;
            }

            body.Add(lines[index]);
            index++;
        }

        int result = 0;

        foreach (string value in iterValues)
        {
            if (_returnRequested)
                break;

            _env.Set(varName, value);
            result = ExecuteBlock(body);

            if (_continueRequested)
            {
                _continueDepth--;
                _continueRequested = _continueDepth > 0;
                if (_continueRequested)
                    break;
                continue;
            }

            if (_breakRequested)
            {
                _breakDepth--;
                _breakRequested = _breakDepth > 0;
                break;
            }
        }

        return result;
    }

    private int ExecuteWhile(List<string> lines, ref int index)
    {
        string line = lines[index].Trim();
        index++;

        string condition = ExtractCondition(line, "while");

        List<string> body = new();
        int depth = 0;
        bool passedDo = false;
        int bodyStart = index;

        while (index < lines.Count)
        {
            string current = lines[index].Trim();

            if (!passedDo)
            {
                if (current == "do" || current.StartsWith("do;"))
                {
                    passedDo = true;
                    index++;
                    continue;
                }
                if (current.StartsWith("do "))
                {
                    passedDo = true;
                    lines[index] = current.Substring(3).Trim();
                    current = lines[index];
                }
            }

            if (current.StartsWith("for ") || current.StartsWith("while ") || current.StartsWith("until "))
                depth++;

            if (current == "done")
            {
                if (depth == 0)
                {
                    index++;
                    break;
                }
                depth--;
            }

            body.Add(lines[index]);
            index++;
        }

        int result = 0;
        int maxIterations = 100000;
        int iteration = 0;

        while (EvaluateCondition(condition) && iteration < maxIterations)
        {
            if (_returnRequested)
                break;

            result = ExecuteBlock(body);
            iteration++;

            if (_continueRequested)
            {
                _continueDepth--;
                _continueRequested = _continueDepth > 0;
                if (_continueRequested)
                    break;
                continue;
            }

            if (_breakRequested)
            {
                _breakDepth--;
                _breakRequested = _breakDepth > 0;
                break;
            }
        }

        return result;
    }

    private int ExecuteUntil(List<string> lines, ref int index)
    {
        string line = lines[index].Trim();
        index++;

        string condition = ExtractCondition(line, "until");

        List<string> body = new();
        int depth = 0;
        bool passedDo = false;

        while (index < lines.Count)
        {
            string current = lines[index].Trim();

            if (!passedDo)
            {
                if (current == "do" || current.StartsWith("do;"))
                {
                    passedDo = true;
                    index++;
                    continue;
                }
                if (current.StartsWith("do "))
                {
                    passedDo = true;
                    lines[index] = current.Substring(3).Trim();
                    current = lines[index];
                }
            }

            if (current.StartsWith("for ") || current.StartsWith("while ") || current.StartsWith("until "))
                depth++;

            if (current == "done")
            {
                if (depth == 0)
                {
                    index++;
                    break;
                }
                depth--;
            }

            body.Add(lines[index]);
            index++;
        }

        int result = 0;
        int maxIterations = 100000;
        int iteration = 0;

        while (!EvaluateCondition(condition) && iteration < maxIterations)
        {
            if (_returnRequested)
                break;

            result = ExecuteBlock(body);
            iteration++;

            if (_continueRequested)
            {
                _continueDepth--;
                _continueRequested = _continueDepth > 0;
                if (_continueRequested)
                    break;
                continue;
            }

            if (_breakRequested)
            {
                _breakDepth--;
                _breakRequested = _breakDepth > 0;
                break;
            }
        }

        return result;
    }

    private int ExecuteCase(List<string> lines, ref int index)
    {
        string line = lines[index].Trim();
        index++;

        string value = "";
        int caseIdx = line.IndexOf("case ");
        int inIdx = line.LastIndexOf(" in");
        if (caseIdx >= 0 && inIdx > caseIdx)
        {
            string rawValue = _env.Expand(ExpandPositionalParams(line.Substring(caseIdx + 5, inIdx - caseIdx - 5).Trim()));
            var valParts = SplitCommandLine(rawValue);
            value = valParts.Length > 0 ? valParts[0] : "";
        }

        bool matched = false;
        int result = 0;
        int depth = 0;

        while (index < lines.Count)
        {
            string current = lines[index].Trim();

            if (current == "esac")
            {
                if (depth == 0)
                {
                    index++;
                    break;
                }
            }

            if (depth == 0 && current.EndsWith(")") && !current.StartsWith("function") && !current.Contains("()"))
            {
                string patternStr = current.Substring(0, current.Length - 1).Trim();
                if (patternStr.StartsWith("("))
                    patternStr = patternStr.Substring(1).Trim();

                bool isMatch = false;
                if (!matched)
                {
                    string[] patterns = patternStr.Split('|', StringSplitOptions.RemoveEmptyEntries);
                    foreach (string p in patterns)
                    {
                        string rawPat = _env.Expand(ExpandPositionalParams(p.Trim()));
                        var patParts = SplitCommandLine(rawPat);
                        string pat = patParts.Length > 0 ? patParts[0] : "";

                        if (pat == "*" || IsWildcardMatch(value, pat))
                        {
                            isMatch = true;
                            break;
                        }
                    }
                }

                index++;
                List<string> block = new();
                int blockDepth = 0;

                while (index < lines.Count)
                {
                    string inner = lines[index].Trim();
                    if (inner.StartsWith("case ")) blockDepth++;
                    else if (inner == "esac")
                    {
                        if (blockDepth > 0) blockDepth--;
                        else break;
                    }
                    else if (inner == ";;")
                    {
                        if (blockDepth == 0)
                        {
                            index++;
                            break;
                        }
                    }

                    block.Add(lines[index]);
                    index++;
                }

                if (isMatch && !matched)
                {
                    matched = true;
                    result = ExecuteBlock(block);
                }
                continue;
            }

            index++;
        }

        return result;
    }

    private bool IsWildcardMatch(string value, string pattern)
    {
        string regexPattern = "^" + System.Text.RegularExpressions.Regex.Escape(pattern)
            .Replace("\\*", ".*")
            .Replace("\\?", ".") + "$";
        try
        {
            return System.Text.RegularExpressions.Regex.IsMatch(value, regexPattern);
        }
        catch { return false; }
    }

    private int ExecuteBlock(List<string> block)
    {
        int blockIndex = 0;
        int lastResult = 0;

        while (blockIndex < block.Count)
        {
            if (_returnRequested)
                return _returnCode;

            if (_breakRequested || _continueRequested)
                return lastResult;

            lastResult = ExecuteLine(block, ref blockIndex);
            _env.LastExitCode = lastResult;
        }

        return lastResult;
    }

    private int ExecuteFunction(FunctionDef func, string callLine)
    {
        string[] callArgs = SplitCommandLine(callLine);
        string[] funcArgs = callArgs.Length > 1 ? callArgs.Skip(1).ToArray() : Array.Empty<string>();

        string[] savedArgs = _scriptArgs;
        string savedName = _scriptName;

        _scriptArgs = funcArgs;
        SetPositionalParams(funcArgs);

        _env.PushScope();
        _env.SetLocal("FUNCNAME", func.Name);
        int result = 0;
        try
        {
            result = ExecuteBlock(func.Body);
        }
        finally
        {
            _env.PopScope();
        }

        _scriptArgs = savedArgs;
        _scriptName = savedName;
        SetPositionalParams(savedArgs);

        if (_returnRequested)
        {
            _returnRequested = false;
            return _returnCode;
        }

        return result;
    }

    private void PreScanFunctions(List<string> lines)
    {
        int i = 0;
        while (i < lines.Count)
        {
            string line = lines[i].Trim();

            if (IsFunctionDefinition(line))
            {
                string name = ExtractFunctionName(line);
                if (!string.IsNullOrEmpty(name))
                {
                    var func = new FunctionDef { Name = name };
                    i++;

                    bool foundOpenBrace = line.Contains('{');
                    if (!foundOpenBrace)
                    {
                        while (i < lines.Count)
                        {
                            string nextLine = lines[i].Trim();
                            if (nextLine == "{")
                            {
                                foundOpenBrace = true;
                                i++;
                                break;
                            }
                            i++;
                        }
                    }
                    else
                    {
                        string afterBrace = line.Substring(line.IndexOf('{') + 1).Trim();
                        if (!string.IsNullOrEmpty(afterBrace) && afterBrace != "}")
                            func.Body.Add(afterBrace);
                    }

                    if (foundOpenBrace)
                    {
                        int depth = 1;
                        while (i < lines.Count && depth > 0)
                        {
                            string bodyLine = lines[i].Trim();

                            foreach (char c in bodyLine)
                            {
                                if (c == '{') depth++;
                                else if (c == '}') depth--;
                            }

                            if (depth > 0)
                                func.Body.Add(lines[i]);
                            else
                            {
                                string beforeClose = bodyLine.Substring(0, bodyLine.LastIndexOf('}'));
                                if (!string.IsNullOrWhiteSpace(beforeClose))
                                    func.Body.Add(beforeClose);
                            }
                            i++;
                        }
                    }

                    _functions[name] = func;
                    continue;
                }
            }
            i++;
        }
    }

    private int SkipFunctionDefinition(List<string> lines, ref int index)
    {
        string line = lines[index].Trim();
        index++;

        bool hasBrace = line.Contains('{');
        if (!hasBrace)
        {
            while (index < lines.Count)
            {
                string nextLine = lines[index].Trim();
                index++;
                if (nextLine == "{")
                {
                    hasBrace = true;
                    break;
                }
            }
        }

        if (hasBrace)
        {
            int depth = 1;
            while (index < lines.Count && depth > 0)
            {
                string bodyLine = lines[index].Trim();
                foreach (char c in bodyLine)
                {
                    if (c == '{') depth++;
                    else if (c == '}') depth--;
                }
                index++;
            }
        }

        return 0;
    }

    private bool EvaluateCondition(string condition)
    {
        condition = condition.Trim();

        if (condition.StartsWith("[") && condition.EndsWith("]"))
        {
            condition = condition.Substring(1, condition.Length - 2).Trim();
            return EvaluateTestExpression(condition);
        }

        if (condition.StartsWith("[[") && condition.EndsWith("]]"))
        {
            condition = condition.Substring(2, condition.Length - 4).Trim();
            return EvaluateTestExpression(condition);
        }

        string expanded = ExpandPositionalParams(condition);
        int result = _executor.Execute(expanded);
        return result == 0;
    }

    private bool EvaluateTestExpression(string expr)
    {
        expr = ExpandPositionalParams(expr);
        string expanded = _env.Expand(expr);
        string[] parts = SplitCommandLine(expanded);

        if (parts.Length == 0)
            return false;

        int pos = 0;
        return ParseTestOr(parts, ref pos);
    }

    private bool ParseTestOr(string[] parts, ref int pos)
    {
        bool result = ParseTestAnd(parts, ref pos);
        while (pos < parts.Length && (parts[pos] == "||" || parts[pos] == "-o"))
        {
            pos++;
            bool right = ParseTestAnd(parts, ref pos);
            result = result || right;
        }
        return result;
    }

    private bool ParseTestAnd(string[] parts, ref int pos)
    {
        bool result = ParseTestPrimary(parts, ref pos);
        while (pos < parts.Length && (parts[pos] == "&&" || parts[pos] == "-a"))
        {
            pos++;
            bool right = ParseTestPrimary(parts, ref pos);
            result = result && right;
        }
        return result;
    }

    private bool ParseTestPrimary(string[] parts, ref int pos)
    {
        if (pos >= parts.Length) return false;

        if (pos + 1 < parts.Length && IsBinaryTestOp(parts[pos + 1]))
        {
            string left = parts[pos];
            string op = parts[pos + 1];
            string right = pos + 2 < parts.Length ? parts[pos + 2] : "";
            pos += 3;
            return EvaluateBinaryTestOp(left, op, right);
        }

        if (parts[pos] == "!")
        {
            pos++;
            return !ParseTestPrimary(parts, ref pos);
        }

        if (parts[pos] == "(")
        {
            pos++;
            bool result = ParseTestOr(parts, ref pos);
            if (pos < parts.Length && parts[pos] == ")") pos++;
            return result;
        }

        if (IsUnaryTestOp(parts[pos]))
        {
            string op = parts[pos];
            string operand = pos + 1 < parts.Length ? parts[pos + 1] : "";
            pos += 2;
            return EvaluateUnaryTestOp(op, operand);
        }

        string val = parts[pos];
        pos++;
        return !string.IsNullOrEmpty(val);
    }

    private bool IsBinaryTestOp(string op) =>
        op == "=" || op == "==" || op == "!=" ||
        op == "-eq" || op == "-ne" || op == "-lt" ||
        op == "-le" || op == "-gt" || op == "-ge";

    private bool IsUnaryTestOp(string op) =>
        op == "-z" || op == "-n" || op == "-f" || op == "-d" ||
        op == "-e" || op == "-r" || op == "-w" || op == "-x" || op == "-s";

    private bool EvaluateBinaryTestOp(string left, string op, string right)
    {
        return op switch
        {
            "=" or "==" => left == right,
            "!=" => left != right,
            "-eq" => IntCompare(left, right, (a, b) => a == b),
            "-ne" => IntCompare(left, right, (a, b) => a != b),
            "-lt" => IntCompare(left, right, (a, b) => a < b),
            "-le" => IntCompare(left, right, (a, b) => a <= b),
            "-gt" => IntCompare(left, right, (a, b) => a > b),
            "-ge" => IntCompare(left, right, (a, b) => a >= b),
            _ => false
        };
    }

    private bool EvaluateUnaryTestOp(string op, string operand)
    {
        return op switch
        {
            "-z" => string.IsNullOrEmpty(operand),
            "-n" => !string.IsNullOrEmpty(operand),
            "-f" => File.Exists(operand),
            "-d" => Directory.Exists(operand),
            "-e" => File.Exists(operand) || Directory.Exists(operand),
            "-r" => File.Exists(operand),
            "-w" => File.Exists(operand),
            "-x" => File.Exists(operand),
            "-s" => File.Exists(operand) && new FileInfo(operand).Length > 0,
            _ => false
        };
    }

    private string ExpandPositionalParams(string line)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < line.Length; i++)
        {
            if (line[i] == '`')
            {
                int close = line.IndexOf('`', i + 1);
                if (close > i)
                {
                    string subcmd = line.Substring(i + 1, close - i - 1);
                    var (exit, output) = _executor.ExecuteCapture(subcmd);
                    sb.Append(output.TrimEnd('\r', '\n'));
                    i = close;
                    continue;
                }
            }

            if (line[i] == '$' && i + 1 < line.Length)
            {
                char next = line[i + 1];

                if (next == '(')
                {
                    int depth = 1;
                    int j = i + 2;
                    while (j < line.Length && depth > 0)
                    {
                        if (line[j] == '(') depth++;
                        else if (line[j] == ')') depth--;
                        if (depth > 0) j++;
                    }
                    if (depth == 0)
                    {
                        string subcmd = line.Substring(i + 2, j - i - 2);
                        var (exit, output) = _executor.ExecuteCapture(subcmd);
                        sb.Append(output.TrimEnd('\r', '\n'));
                        i = j;
                        continue;
                    }
                }

                if (next == '@')
                {
                    sb.Append(string.Join(" ", _scriptArgs));
                    i++;
                    continue;
                }

                if (next == '*')
                {
                    sb.Append(string.Join(" ", _scriptArgs));
                    i++;
                    continue;
                }

                if (next == '#')
                {
                    sb.Append(_scriptArgs.Length);
                    i++;
                    continue;
                }

                if (next == '0')
                {
                    sb.Append(_scriptName);
                    i++;
                    continue;
                }

                if (char.IsDigit(next) && next != '0')
                {
                    int paramIdx = next - '1';
                    sb.Append(paramIdx < _scriptArgs.Length ? _scriptArgs[paramIdx] : "");
                    i++;
                    continue;
                }

                if (next == '{')
                {
                    int close = line.IndexOf('}', i + 2);
                    if (close > 0)
                    {
                        string content = line.Substring(i + 2, close - i - 2);
                        if (content == "@" || content == "*")
                        {
                            sb.Append(string.Join(" ", _scriptArgs));
                            i = close;
                            continue;
                        }
                        if (content == "#")
                        {
                            sb.Append(_scriptArgs.Length);
                            i = close;
                            continue;
                        }
                        if (int.TryParse(content, out int num))
                        {
                            if (num == 0)
                                sb.Append(_scriptName);
                            else
                            {
                                int idx = num - 1;
                                sb.Append(idx < _scriptArgs.Length ? _scriptArgs[idx] : "");
                            }
                            i = close;
                            continue;
                        }
                    }
                }
            }

            sb.Append(line[i]);
        }

        return sb.ToString();
    }

    private void SetPositionalParams(string[] args)
    {
        _env.Set("0", _scriptName);
        _env.Set("#", args.Length.ToString());
        _env.Set("@", string.Join(" ", args));
        _env.Set("*", string.Join(" ", args));

        for (int i = 0; i < args.Length && i < 9; i++)
            _env.Set((i + 1).ToString(), args[i]);

        for (int i = args.Length; i < 9; i++)
            _env.Unset((i + 1).ToString());
    }

    private static string ExtractCondition(string line, string keyword)
    {
        string after = line.Substring(keyword.Length).Trim();

        if (after.EndsWith("; then"))
            after = after.Substring(0, after.Length - 6).Trim();
        else if (after.EndsWith(";then"))
            after = after.Substring(0, after.Length - 5).Trim();
        else if (after.EndsWith("; do"))
            after = after.Substring(0, after.Length - 4).Trim();
        else if (after.EndsWith(";do"))
            after = after.Substring(0, after.Length - 3).Trim();
        else if (after.EndsWith(";"))
            after = after.Substring(0, after.Length - 1).Trim();

        return after;
    }

    private static bool IsFunctionDefinition(string line)
    {
        if (line.StartsWith("function "))
            return true;

        int parenIdx = line.IndexOf("()");
        if (parenIdx > 0)
        {
            string name = line.Substring(0, parenIdx).Trim();
            return name.Length > 0 && name.All(c => char.IsLetterOrDigit(c) || c == '_');
        }

        return false;
    }

    private static string ExtractFunctionName(string line)
    {
        if (line.StartsWith("function "))
        {
            string rest = line.Substring(9).Trim();
            int parenIdx = rest.IndexOf('(');
            int braceIdx = rest.IndexOf('{');
            int spaceIdx = rest.IndexOf(' ');

            int endIdx = rest.Length;
            if (parenIdx >= 0) endIdx = Math.Min(endIdx, parenIdx);
            if (braceIdx >= 0) endIdx = Math.Min(endIdx, braceIdx);
            if (spaceIdx >= 0) endIdx = Math.Min(endIdx, spaceIdx);

            return rest.Substring(0, endIdx).Trim();
        }

        int parenPos = line.IndexOf("()");
        if (parenPos > 0)
            return line.Substring(0, parenPos).Trim();

        return "";
    }

    private static string GetCommandName(string line)
    {
        string trimmed = line.Trim();
        int spaceIdx = trimmed.IndexOf(' ');
        return spaceIdx >= 0 ? trimmed.Substring(0, spaceIdx) : trimmed;
    }

    private static List<string> SplitValues(string values)
    {
        var result = new List<string>();
        var current = new StringBuilder();
        bool inSingle = false;
        bool inDouble = false;

        for (int i = 0; i < values.Length; i++)
        {
            char c = values[i];

            if (c == '\'' && !inDouble)
            {
                inSingle = !inSingle;
                continue;
            }

            if (c == '"' && !inSingle)
            {
                inDouble = !inDouble;
                continue;
            }

            if (c == ' ' && !inSingle && !inDouble)
            {
                if (current.Length > 0)
                {
                    result.Add(current.ToString());
                    current.Clear();
                }
                continue;
            }

            current.Append(c);
        }

        if (current.Length > 0)
            result.Add(current.ToString());

        return result;
    }

    private static List<string> SplitLines(string content)
    {
        var lines = new List<string>();
        var current = new StringBuilder();
        bool inSingle = false;
        bool inDouble = false;

        for (int i = 0; i < content.Length; i++)
        {
            char c = content[i];

            if (c == '\\' && i + 1 < content.Length && content[i + 1] == '\n' && !inSingle)
            {
                i++;
                continue;
            }

            if (c == '\'' && !inDouble)
                inSingle = !inSingle;
            else if (c == '"' && !inSingle)
                inDouble = !inDouble;

            if ((c == '\n' || c == ';') && !inSingle && !inDouble)
            {
                lines.Add(current.ToString());
                current.Clear();
            }
            else if (c == '\r')
            {
                continue;
            }
            else
            {
                current.Append(c);
            }
        }

        if (current.Length > 0)
            lines.Add(current.ToString());

        return lines;
    }

    private static string[] SplitCommandLine(string line)
    {
        var args = new List<string>();
        var current = new StringBuilder();
        bool inSingle = false;
        bool inDouble = false;

        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];

            if (c == '\\' && i + 1 < line.Length && !inSingle)
            {
                current.Append(line[i + 1]);
                i++;
                continue;
            }

            if (c == '\'' && !inDouble)
            {
                inSingle = !inSingle;
                continue;
            }

            if (c == '"' && !inSingle)
            {
                inDouble = !inDouble;
                continue;
            }

            if (c == ' ' && !inSingle && !inDouble)
            {
                if (current.Length > 0)
                {
                    args.Add(current.ToString());
                    current.Clear();
                }
                continue;
            }

            current.Append(c);
        }

        if (current.Length > 0)
            args.Add(current.ToString());

        return args.ToArray();
    }

    private static bool IntCompare(string left, string right, Func<int, int, bool> predicate)
    {
        if (int.TryParse(left, out int l) && int.TryParse(right, out int r))
            return predicate(l, r);
        return false;
    }
}
