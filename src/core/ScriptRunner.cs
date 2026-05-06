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

        if (line == "break" || line == "continue")
        {
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

            if (!passedDo && (current == "do" || current.StartsWith("do;")))
            {
                passedDo = true;
                index++;
                continue;
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

            if (!passedDo && (current == "do" || current.StartsWith("do;")))
            {
                passedDo = true;
                index++;
                continue;
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

            if (!passedDo && (current == "do" || current.StartsWith("do;")))
            {
                passedDo = true;
                index++;
                continue;
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
        }

        return result;
    }

    private int ExecuteBlock(List<string> block)
    {
        int blockIndex = 0;
        int lastResult = 0;

        while (blockIndex < block.Count)
        {
            if (_returnRequested)
                return _returnCode;

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

        int result = ExecuteBlock(func.Body);

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

        if (parts.Length == 1)
            return !string.IsNullOrEmpty(parts[0]);

        if (parts.Length == 2)
        {
            string op = parts[0];
            string operand = parts[1];

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
                "!" => string.IsNullOrEmpty(operand),
                _ => false
            };
        }

        if (parts.Length == 3)
        {
            string left = parts[0];
            string op = parts[1];
            string right = parts[2];

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

        if (parts.Length == 4 && parts[0] == "!")
        {
            string innerExpr = string.Join(" ", parts.Skip(1));
            return !EvaluateTestExpression(innerExpr);
        }

        return false;
    }

    private string ExpandPositionalParams(string line)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < line.Length; i++)
        {
            if (line[i] == '$' && i + 1 < line.Length)
            {
                char next = line[i + 1];

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

            if (c == '\n' && !inSingle && !inDouble)
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
