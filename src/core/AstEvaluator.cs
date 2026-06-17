using System;
using System.Collections.Generic;
using System.IO;

namespace AurShell.Core;

public class AstEvaluator
{
    private readonly ShellEnvironment _env;
    private readonly Executor _executor;
    private string _workingDirectory;
    private bool _breakRequested;
    private bool _continueRequested;
    private bool _returnRequested;
    private int _breakDepth;
    private int _continueDepth;
    private int _loopDepth;

    private static readonly System.Threading.AsyncLocal<Stream?> _asyncOut = new();
    private static readonly System.Threading.AsyncLocal<Stream?> _asyncIn = new();
    private static readonly System.Threading.AsyncLocal<Stream?> _asyncErr = new();

    public static Stream? OutStream => _asyncOut.Value;
    public static Stream? InStream => _asyncIn.Value;
    public static Stream? ErrStream => _asyncErr.Value;

    public AstEvaluator(ShellEnvironment env, Executor executor, string workingDirectory)
    {
        _env = env;
        _executor = executor;
        _workingDirectory = workingDirectory;
    }

    public static void RunWithStreams(Stream? inStream, Stream? outStream, Stream? errStream, Action action)
    {
        var prevIn = _asyncIn.Value;
        var prevOut = _asyncOut.Value;
        var prevErr = _asyncErr.Value;
        
        _asyncIn.Value = inStream;
        _asyncOut.Value = outStream;
        _asyncErr.Value = errStream;
        
        try { action(); }
        finally {
            _asyncIn.Value = prevIn;
            _asyncOut.Value = prevOut;
            _asyncErr.Value = prevErr;
        }
    }

    public int Visit(ListNode list)
    {
        int lastExit = 0;

        for (int i = 0; i < list.Entries.Count; i++)
        {
            var entry = list.Entries[i];

            if (i > 0)
            {
                var prevOp = list.Entries[i - 1].Operator;
                if (prevOp == ListOperator.And && lastExit != 0) continue;
                if (prevOp == ListOperator.Or && lastExit == 0) continue;
            }

            lastExit = Visit(entry.Pipeline);
            _env.LastExitCode = lastExit;

            if (_breakRequested || _continueRequested || _returnRequested)
                break;
        }

        return lastExit;
    }

    public int Visit(PipelineNode pipeline)
    {
        if (pipeline.Commands.Count == 0) return 0;
        
        if (pipeline.Commands.Count == 1)
        {
            return Visit(pipeline.Commands[0]);
        }

        // Natively implement piped execution of AST nodes concurrently
        int count = pipeline.Commands.Count;
        var tasks = new System.Threading.Tasks.Task<int>[count];
        var outStreams = new System.IO.Pipes.AnonymousPipeServerStream[count - 1];
        var inStreams = new System.IO.Pipes.AnonymousPipeClientStream[count - 1];

        for (int i = 0; i < count - 1; i++)
        {
            outStreams[i] = new System.IO.Pipes.AnonymousPipeServerStream(System.IO.Pipes.PipeDirection.Out);
            inStreams[i] = new System.IO.Pipes.AnonymousPipeClientStream(System.IO.Pipes.PipeDirection.In, outStreams[i].GetClientHandleAsString());
        }

        for (int i = 0; i < count; i++)
        {
            int index = i;
            var cmd = pipeline.Commands[index];
            var currentIn = index == 0 ? _asyncIn.Value : inStreams[index - 1];
            var currentOut = index == count - 1 ? _asyncOut.Value : outStreams[index];
            var currentErr = _asyncErr.Value; // inherit stderr

            tasks[index] = System.Threading.Tasks.Task.Run(() =>
            {
                _asyncIn.Value = currentIn;
                _asyncOut.Value = currentOut;
                _asyncErr.Value = currentErr;

                try
                {
                    return Visit(cmd);
                }
                catch (System.IO.IOException ex) when (ex.Message.Contains("broken", StringComparison.OrdinalIgnoreCase) || ex.Message.Contains("closed", StringComparison.OrdinalIgnoreCase))
                {
                    // Simulated SIGPIPE
                    return 141; 
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"aursh: pipeline error: {ex.Message}");
                    return 1;
                }
                finally
                {
                    if (index < count - 1)
                    {
                        outStreams[index].Dispose();
                    }
                    if (index > 0)
                    {
                        inStreams[index - 1].Dispose();
                    }
                }
            });
        }

        if (pipeline.Background)
        {
            return 0; // Background tasks run detached
        }

        System.Threading.Tasks.Task.WaitAll(tasks);
        return tasks[count - 1].Result;
    }

    public int Visit(ICommandNode node)
    {
        return node switch
        {
            SimpleCommandNode scn => ExecuteSimpleCommand(scn),
            IfNode inod => ExecuteWithRedirections(inod, () => Visit(inod)),
            WhileNode wn => ExecuteWithRedirections(wn, () => Visit(wn)),
            UntilNode un => ExecuteWithRedirections(un, () => Visit(un)),
            ForNode fn => ExecuteWithRedirections(fn, () => Visit(fn)),
            CaseNode cn => ExecuteWithRedirections(cn, () => Visit(cn)),
            BlockNode bn => ExecuteWithRedirections(bn, () => Visit(bn)),
            SubshellNode ssn => ExecuteWithRedirections(ssn, () => Visit(ssn)),
            FunctionNode fnod => Visit(fnod),
            AssignmentNode an => Visit(an),
            ArrayAssignmentNode aan => Visit(aan),
            _ => 1
        };
    }

    private int ExecuteSimpleCommand(SimpleCommandNode cmd)
    {
        var allRaw = new List<string>();
        if (!string.IsNullOrEmpty(cmd.RawExpandedName)) allRaw.Add(cmd.RawExpandedName);
        allRaw.AddRange(cmd.RawExpandedArgs);

        string rawCommandStr = string.Join(" ", allRaw);
        if (_executor.TryParseArithmeticOrAssignment(rawCommandStr))
        {
            return 0;
        }

        var expandedArgs = new List<string>();
        foreach (var arg in allRaw)
        {
            expandedArgs.AddRange(WordExpander.ExpandWord(arg, _env));
        }

        if (expandedArgs.Count == 0) return 0;

        var execCmd = new SimpleCommandNode
        {
            Line = cmd.Line,
            Column = cmd.Column,
            Name = expandedArgs[0]
        };
        execCmd.Args.AddRange(expandedArgs.Skip(1));
        execCmd.Redirections.AddRange(cmd.Redirections);

        var savedAssignments = ApplyPrefixAssignments(cmd.PrefixAssignments);
        try
        {
            if (execCmd.Name == "break")
            {
                if (_loopDepth == 0)
                {
                    Console.Error.WriteLine("aursh: break: only meaningful in a loop");
                    return 1;
                }

                if (!TryGetFlowDepth(execCmd, out _breakDepth))
                    return 1;

                _breakRequested = true;
                return 0;
            }
            if (execCmd.Name == "continue")
            {
                if (_loopDepth == 0)
                {
                    Console.Error.WriteLine("aursh: continue: only meaningful in a loop");
                    return 1;
                }

                if (!TryGetFlowDepth(execCmd, out _continueDepth))
                    return 1;

                _continueRequested = true;
                return 0;
            }
            if (execCmd.Name == "return")
            {
                _returnRequested = true;
                if (execCmd.Args.Count > 0 && int.TryParse(execCmd.Args[0], out int retCode))
                    return retCode;
                return 0;
            }

            // Before executing an external command or builtin, check if it's a user-defined function.
            if (!string.IsNullOrEmpty(execCmd.Name))
            {
                var funcBody = _env.GetFunction(execCmd.Name);
                if (funcBody != null)
                {
                    return ExecuteWithRedirections(execCmd, () => {
                        _env.PushFrame(new StackFrame(execCmd.Name, execCmd.Line, execCmd.Column, FrameType.Function));
                        _env.PushScope();
                        _env.PushPositionalArguments(execCmd.Args);
                        
                        int exitCode = 1;
                        try
                        {
                            exitCode = Visit(funcBody);
                        }
                        catch (Exception ex)
                        {
                            Console.Error.WriteLine($"aursh: error in function '{execCmd.Name}': {ex.Message}");
                            _env.PrintCallStack();
                        }
                        finally
                        {
                            _env.PopPositionalArguments();
                            _env.PopScope();
                            _env.PopFrame();
                            _returnRequested = false; // Reset return flag after function exit
                        }
                        return exitCode;
                    });
                }
            }

            return Pipeline.ExecuteSingle(execCmd, _env, _workingDirectory, false, this, _asyncIn.Value, _asyncOut.Value, _asyncErr.Value);
        }
        finally
        {
            RestorePrefixAssignments(savedAssignments);
        }
    }

    private List<(string Name, bool HadValue, string? OldValue)> ApplyPrefixAssignments(List<AssignmentNode> assignments)
    {
        var saved = new List<(string Name, bool HadValue, string? OldValue)>();

        foreach (var assignment in assignments)
        {
            bool hadValue = _env.Variables.ContainsKey(assignment.VariableName);
            string? oldValue = hadValue ? _env.Get(assignment.VariableName) : null;
            saved.Add((assignment.VariableName, hadValue, oldValue));

            string value = string.Join(" ", WordExpander.ExpandWord(assignment.RawExpandedValue, _env));
            _env.Set(assignment.VariableName, value);
        }

        return saved;
    }

    private void RestorePrefixAssignments(List<(string Name, bool HadValue, string? OldValue)> saved)
    {
        for (int i = saved.Count - 1; i >= 0; i--)
        {
            var item = saved[i];
            if (item.HadValue)
                _env.Set(item.Name, item.OldValue ?? "");
            else
                _env.Unset(item.Name);
        }
    }

    private int ExecuteWithRedirections(ICommandNode node, Func<int> execute)
    {
        if (node.Redirections.Count == 0)
            return execute();

        Stream? stdinStream = null;
        Stream? stdoutStream = null;
        Stream? stderrStream = null;
        TextReader? originalIn = null;
        TextWriter? originalOut = null;
        TextWriter? originalErr = null;

        Stream? nextIn = _asyncIn.Value;
        Stream? nextOut = _asyncOut.Value;
        Stream? nextErr = _asyncErr.Value;

        try
        {
            foreach (var redir in node.Redirections)
            {
                string target = ExpandRedirectionTarget(redir.Target);
                switch (redir.Type)
                {
                    case RedirectType.In:
                        stdinStream = new FileStream(AurShell.Utils.FileSystem.ResolvePath(target, _workingDirectory), FileMode.Open, FileAccess.Read);
                        nextIn = stdinStream;
                        originalIn ??= Console.In;
                        Console.SetIn(new StreamReader(stdinStream));
                        break;
                    case RedirectType.HereString:
                    case RedirectType.HereDoc:
                        stdinStream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(target + (redir.Type == RedirectType.HereString ? "\n" : "")));
                        nextIn = stdinStream;
                        originalIn ??= Console.In;
                        Console.SetIn(new StreamReader(stdinStream));
                        break;
                    case RedirectType.Out:
                    case RedirectType.Append:
                        stdoutStream = new FileStream(
                            AurShell.Utils.FileSystem.ResolvePath(target, _workingDirectory),
                            redir.Type == RedirectType.Out ? FileMode.Create : FileMode.Append,
                            FileAccess.Write);
                        nextOut = stdoutStream;
                        originalOut ??= Console.Out;
                        Console.SetOut(new StreamWriter(stdoutStream) { AutoFlush = true });
                        break;
                    case RedirectType.Err:
                    case RedirectType.ErrAppend:
                        stderrStream = new FileStream(
                            AurShell.Utils.FileSystem.ResolvePath(target, _workingDirectory),
                            redir.Type == RedirectType.Err ? FileMode.Create : FileMode.Append,
                            FileAccess.Write);
                        nextErr = stderrStream;
                        originalErr ??= Console.Error;
                        Console.SetError(new StreamWriter(stderrStream) { AutoFlush = true });
                        break;
                    case RedirectType.ErrToOut:
                        nextErr = nextOut;
                        originalErr ??= Console.Error;
                        Console.SetError(Console.Out);
                        break;
                }
            }

            int exitCode = 0;
            RunWithStreams(nextIn, nextOut, nextErr, () => exitCode = execute());
            return exitCode;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"aursh: redirection error: {ex.Message}");
            return 1;
        }
        finally
        {
            if (originalIn != null) Console.SetIn(originalIn);
            if (originalOut != null) Console.SetOut(originalOut);
            if (originalErr != null) Console.SetError(originalErr);
            stdinStream?.Dispose();
            stdoutStream?.Dispose();
            stderrStream?.Dispose();
        }
    }

    private string ExpandRedirectionTarget(string target)
    {
        var expanded = WordExpander.ExpandWord(target, _env);
        return expanded.Count > 0 ? expanded[0] : target;
    }

    private int Visit(SubshellNode node)
    {
        var clonedEnv = _env.Clone();
        clonedEnv.PushFrame(new StackFrame("subshell", node.Line, node.Column, FrameType.Subshell));
        var subEval = new AstEvaluator(clonedEnv, _executor, _workingDirectory);
        int exitCode = 1;
        try
        {
            exitCode = subEval.Visit(node.Body);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"aursh: error in subshell: {ex.Message}");
            clonedEnv.PrintCallStack();
        }
        finally
        {
            clonedEnv.PopFrame();
        }
        _env.LastExitCode = exitCode;
        return exitCode;
    }

    private int Visit(FunctionNode node)
    {
        _env.SetFunction(node.Name, node.Body);
        return 0;
    }

    private int Visit(AssignmentNode node)
    {
        string expanded = string.Join(" ", WordExpander.ExpandWord(node.RawExpandedValue, _env));
        _env.Set(node.VariableName, expanded);
        return 0;
    }

    private int Visit(ArrayAssignmentNode node)
    {
        var expanded = new List<string>();
        foreach (var val in node.Values)
        {
            expanded.AddRange(WordExpander.ExpandWord(val, _env));
        }
        _env.SetArray(node.VariableName, expanded);
        return 0;
    }

    private int Visit(IfNode node)
    {
        int exit = Visit(node.Condition);
        if (exit == 0)
        {
            return Visit(node.ThenBlock);
        }

        foreach (var elif in node.ElifBlocks)
        {
            if (Visit(elif.condition) == 0)
            {
                return Visit(elif.block);
            }
        }

        if (node.ElseBlock != null)
        {
            return Visit(node.ElseBlock);
        }

        return 0;
    }

    private int Visit(WhileNode node)
    {
        int lastExit = 0;
        _loopDepth++;
        try
        {
            while (Visit(node.Condition) == 0)
            {
                if (_returnRequested) break;

                lastExit = Visit(node.Body);

                if (HandleContinue())
                {
                    if (_continueRequested) break;
                    continue;
                }

                if (HandleBreak())
                    break;
            }
        }
        finally
        {
            _loopDepth--;
        }
        return lastExit;
    }

    private int Visit(UntilNode node)
    {
        int lastExit = 0;
        _loopDepth++;
        try
        {
            while (Visit(node.Condition) != 0)
            {
                if (_returnRequested) break;

                lastExit = Visit(node.Body);

                if (HandleContinue())
                {
                    if (_continueRequested) break;
                    continue;
                }

                if (HandleBreak())
                    break;
            }
        }
        finally
        {
            _loopDepth--;
        }
        return lastExit;
    }

    private int Visit(ForNode node)
    {
        int lastExit = 0;
        
        var expandedValues = new List<string>();
        if (node.IteratorValues.Count == 0)
        {
            expandedValues.AddRange(_env.PositionalArguments);
        }
        else
        {
            foreach (var val in node.IteratorValues)
            {
                expandedValues.AddRange(WordExpander.ExpandWord(val, _env));
            }
        }

        _loopDepth++;
        try
        {
            foreach (var val in expandedValues)
            {
                if (_returnRequested) break;

                _env.Set(node.VariableName, val);
                lastExit = Visit(node.Body);

                if (HandleContinue())
                {
                    if (_continueRequested) break;
                    continue;
                }

                if (HandleBreak())
                    break;
            }
        }
        finally
        {
            _loopDepth--;
        }
        return lastExit;
    }

    private bool TryGetFlowDepth(SimpleCommandNode cmd, out int depth)
    {
        depth = 1;

        if (cmd.Args.Count == 0)
            return true;

        if (!int.TryParse(cmd.Args[0], out depth) || depth < 1)
        {
            Console.Error.WriteLine($"aursh: {cmd.Name}: {cmd.Args[0]}: numeric argument required");
            return false;
        }

        return true;
    }

    private bool HandleBreak()
    {
        if (!_breakRequested)
            return false;

        _breakDepth--;
        if (_breakDepth <= 0 || _loopDepth <= 1)
            _breakRequested = false;

        return true;
    }

    private bool HandleContinue()
    {
        if (!_continueRequested)
            return false;

        _continueDepth--;
        if (_continueDepth <= 0 || _loopDepth <= 1)
        {
            _continueRequested = false;
        }

        return true;
    }

    private int Visit(CaseNode node)
    {
        string value = string.Join(" ", WordExpander.ExpandWord(node.Value, _env));
        foreach (var c in node.Cases)
        {
            bool match = false;
            foreach (var pat in c.Patterns)
            {
                string expandedPat = string.Join(" ", WordExpander.ExpandWord(pat, _env, performGlobbing: false));
                if (IsGlobMatch(value, expandedPat))
                {
                    match = true;
                    break;
                }
            }
            if (match)
            {
                return Visit(c.Body);
            }
        }
        return 0;
    }

    private int Visit(BlockNode node)
    {
        return Visit(node.Body);
    }

    private bool IsGlobMatch(string value, string pattern)
    {
        try
        {
            string regexPattern = "^" + GlobToRegex(pattern) + "$";
            return System.Text.RegularExpressions.Regex.IsMatch(value, regexPattern);
        }
        catch
        {
            // Fallback to literal match if pattern is structurally invalid as a regex class
            return value == pattern;
        }
    }

    private string GlobToRegex(string pattern)
    {
        var sb = new System.Text.StringBuilder();
        bool escape = false;
        bool inClass = false;

        for (int i = 0; i < pattern.Length; i++)
        {
            char c = pattern[i];

            if (escape)
            {
                sb.Append(System.Text.RegularExpressions.Regex.Escape(c.ToString()));
                escape = false;
                continue;
            }

            if (c == '\\')
            {
                escape = true;
                continue;
            }

            if (inClass)
            {
                if (c == ']')
                {
                    sb.Append(']');
                    inClass = false;
                }
                else if (c == '!' && i > 0 && pattern[i - 1] == '[')
                {
                    sb.Append('^'); // Negation in glob is !, in regex is ^
                }
                else
                {
                    sb.Append(c);
                }
            }
            else
            {
                switch (c)
                {
                    case '*':
                        sb.Append(".*");
                        break;
                    case '?':
                        sb.Append(".");
                        break;
                    case '[':
                        sb.Append('[');
                        inClass = true;
                        break;
                    default:
                        sb.Append(System.Text.RegularExpressions.Regex.Escape(c.ToString()));
                        break;
                }
            }
        }

        if (escape)
        {
            sb.Append(System.Text.RegularExpressions.Regex.Escape("\\"));
        }
        
        return sb.ToString();
    }

    private List<string> SplitWordsByIFS(string input, string ifs)
    {
        var result = new List<string>();
        if (string.IsNullOrEmpty(input)) return result;

        if (string.IsNullOrEmpty(ifs))
        {
            result.Add(input);
            return result;
        }

        bool isWhitespaceIfs(char c) => (c == ' ' || c == '\t' || c == '\n') && ifs.Contains(c);
        bool isNonWhitespaceIfs(char c) => !isWhitespaceIfs(c) && ifs.Contains(c);

        int pos = 0;

        // Ignore leading IFS whitespace
        while (pos < input.Length && isWhitespaceIfs(input[pos]))
            pos++;

        if (pos >= input.Length)
            return result;

        var currentField = new System.Text.StringBuilder();

        while (pos < input.Length)
        {
            char c = input[pos];

            if (isWhitespaceIfs(c))
            {
                // Sequence of IFS whitespace is a single delimiter
                while (pos < input.Length && isWhitespaceIfs(input[pos]))
                    pos++;

                // If it's the end of string, we don't add an empty trailing field
                if (pos >= input.Length)
                {
                    result.Add(currentField.ToString());
                    break;
                }

                // If followed by an IFS non-whitespace, absorb the whitespace
                if (isNonWhitespaceIfs(input[pos]))
                {
                    result.Add(currentField.ToString());
                    currentField.Clear();
                    pos++; // consume the non-whitespace
                    // Absorb trailing whitespace after non-whitespace
                    while (pos < input.Length && isWhitespaceIfs(input[pos]))
                        pos++;
                }
                else
                {
                    // It was just whitespace acting as a delimiter
                    result.Add(currentField.ToString());
                    currentField.Clear();
                }
            }
            else if (isNonWhitespaceIfs(c))
            {
                result.Add(currentField.ToString());
                currentField.Clear();
                pos++;
                // Absorb trailing whitespace after non-whitespace
                while (pos < input.Length && isWhitespaceIfs(input[pos]))
                    pos++;
            }
            else
            {
                currentField.Append(c);
                pos++;
            }
        }

        if (pos <= input.Length && currentField.Length > 0)
        {
            result.Add(currentField.ToString());
        }
        else if (pos > 0 && isNonWhitespaceIfs(input[pos - 1]))
        {
             // If string ends exactly on a non-whitespace IFS (and optional whitespace), 
             // it generates a final empty field.
             result.Add("");
        }

        return result;
    }
}
