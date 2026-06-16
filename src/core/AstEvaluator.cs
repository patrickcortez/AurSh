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

    private static readonly System.Threading.AsyncLocal<Stream?> _asyncOut = new();
    private static readonly System.Threading.AsyncLocal<Stream?> _asyncIn = new();
    private static readonly System.Threading.AsyncLocal<Stream?> _asyncErr = new();

    public AstEvaluator(ShellEnvironment env, Executor executor, string workingDirectory)
    {
        _env = env;
        _executor = executor;
        _workingDirectory = workingDirectory;
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
            IfNode inod => Visit(inod),
            WhileNode wn => Visit(wn),
            UntilNode un => Visit(un),
            ForNode fn => Visit(fn),
            CaseNode cn => Visit(cn),
            BlockNode bn => Visit(bn),
            SubshellNode ssn => Visit(ssn),
            FunctionNode fnod => Visit(fnod),
            AssignmentNode an => Visit(an),
            ArrayAssignmentNode aan => Visit(aan),
            _ => 1
        };
    }

    private int ExecuteSimpleCommand(SimpleCommandNode cmd)
    {
        if (cmd.Name == "break")
        {
            _breakRequested = true;
            return 0;
        }
        if (cmd.Name == "continue")
        {
            _continueRequested = true;
            return 0;
        }
        if (cmd.Name == "return")
        {
            _returnRequested = true;
            if (cmd.Args.Count > 0 && int.TryParse(cmd.Args[0], out int retCode))
                return retCode;
            return 0;
        }

        // Before executing an external command or builtin, check if it's a user-defined function.
        if (!string.IsNullOrEmpty(cmd.Name))
        {
            var funcBody = _env.GetFunction(cmd.Name);
            if (funcBody != null)
            {
                _env.PushFrame(new StackFrame(cmd.Name, cmd.Line, cmd.Column, FrameType.Function));
                _env.PushScope();
                for (int i = 0; i < cmd.Args.Count; i++)
                {
                    _env.SetLocal((i + 1).ToString(), cmd.Args[i]);
                }
                _env.SetLocal("#", cmd.Args.Count.ToString());
                _env.SetLocal("@", string.Join(" ", cmd.Args));
                _env.SetLocal("*", string.Join(" ", cmd.Args));
                
                int exitCode = 1;
                try
                {
                    exitCode = Visit(funcBody);
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"aursh: error in function '{cmd.Name}': {ex.Message}");
                    _env.PrintCallStack();
                }
                finally
                {
                    _env.PopScope();
                    _env.PopFrame();
                    _returnRequested = false; // Reset return flag after function exit
                }
                return exitCode;
            }
        }

        return Pipeline.ExecuteSingle(cmd, _env, _workingDirectory, false, this, _asyncIn.Value, _asyncOut.Value, _asyncErr.Value);
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
        string expanded = _env.Expand(node.Value);
        _env.Set(node.VariableName, expanded);
        return 0;
    }

    private int Visit(ArrayAssignmentNode node)
    {
        var expanded = new List<string>();
        foreach (var val in node.Values)
        {
            expanded.Add(_env.Expand(val));
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
        while (Visit(node.Condition) == 0)
        {
            if (_returnRequested) break;

            lastExit = Visit(node.Body);

            if (_continueRequested)
            {
                _continueDepth--;
                _continueRequested = _continueDepth > 0;
                if (_continueRequested) break;
                continue;
            }

            if (_breakRequested)
            {
                _breakDepth--;
                _breakRequested = _breakDepth > 0;
                break;
            }
        }
        return lastExit;
    }

    private int Visit(UntilNode node)
    {
        int lastExit = 0;
        while (Visit(node.Condition) != 0)
        {
            if (_returnRequested) break;

            lastExit = Visit(node.Body);

            if (_continueRequested)
            {
                _continueDepth--;
                _continueRequested = _continueDepth > 0;
                if (_continueRequested) break;
                continue;
            }

            if (_breakRequested)
            {
                _breakDepth--;
                _breakRequested = _breakDepth > 0;
                break;
            }
        }
        return lastExit;
    }

    private int Visit(ForNode node)
    {
        int lastExit = 0;
        
        var expandedValues = new List<string>();
        string ifs = _env.Variables.ContainsKey("IFS") ? _env.Get("IFS") ?? "" : " \t\n";

        foreach (var val in node.IteratorValues)
        {
            if (val == "$@")
            {
                for (int i = 1; i <= _env.PositionalArguments.Count; i++)
                {
                    expandedValues.Add(_env.PositionalArguments[i - 1]);
                }
            }
            else if (val == "$*")
            {
                if (_env.PositionalArguments.Count > 0)
                {
                    string joined = string.Join(ifs.Length > 0 ? ifs[0].ToString() : " ", _env.PositionalArguments);
                    expandedValues.AddRange(SplitWordsByIFS(joined, ifs));
                }
            }
            else
            {
                var expanded = _env.Expand(val);
                expandedValues.AddRange(SplitWordsByIFS(expanded, ifs));
            }
        }

        foreach (var val in expandedValues)
        {
            if (_returnRequested) break;

            _env.Set(node.VariableName, val);
            lastExit = Visit(node.Body);

            if (_continueRequested)
            {
                _continueDepth--;
                _continueRequested = _continueDepth > 0;
                if (_continueRequested) break;
                continue;
            }

            if (_breakRequested)
            {
                _breakDepth--;
                _breakRequested = _breakDepth > 0;
                break;
            }
        }
        return lastExit;
    }

    private int Visit(CaseNode node)
    {
        string value = _env.Expand(node.Value);
        foreach (var c in node.Cases)
        {
            bool match = false;
            foreach (var pat in c.Patterns)
            {
                string expandedPat = _env.Expand(pat);
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
