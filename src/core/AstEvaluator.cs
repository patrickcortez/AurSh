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

    public string WorkingDirectory => _workingDirectory;

    public void UpdateWorkingDirectory(string newDir)
    {
        _workingDirectory = newDir;
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
        finally
        {
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
        if (_env.Debugger != null && _env.Debugger.ShouldPause(node.Line))
        {
            _env.Debugger.PauseAndBlock(node.Line, _env);
        }

        return node switch
        {
            SimpleCommandNode scn => ExecuteSimpleCommand(scn),
            PipelineNode pn => Pipeline.ExecutePipeline(pn, _env, _workingDirectory, this),
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
            LetNode ln => Visit(ln),
            ConstNode cnn => Visit(cnn),
            AST.ExpressionStatementNode esn => Visit(esn),
            TryCatchNode tcn => ExecuteWithRedirections(tcn, () => Visit(tcn)),
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
                if (execCmd.Args.Count > 0)
                {
                    // If it parses purely as int, set exit code too for backward compatibility
                    if (int.TryParse(execCmd.Args[0], out int retCode))
                    {
                        _env.LastReturnValue = new Types.AurInt(retCode);
                        return retCode;
                    }
                    else
                    {
                        string returnVal = string.Join(" ", execCmd.Args);
                        _env.LastReturnValue = new Types.AurString(returnVal);
                        return 0;
                    }
                }
                
                _env.LastReturnValue = null;
                return 0;
            }
            if (execCmd.Name == "throw")
            {
                string errMsg = string.Join(" ", execCmd.Args);
                throw new Exception(errMsg); // Bubble up to try/catch or exit
            }
            if (execCmd.Name == "export")
            {
                if (execCmd.Args.Count == 0) return 0;
                string innerCmdStr = string.Join(" ", cmd.RawExpandedArgs);
                var lexer = new Lexer(innerCmdStr, _env);
                var innerCmd = new Parser(lexer.Tokenize()).Parse();
                
                int result = 0;
                if (innerCmd != null && innerCmd is ListNode listNode)
                {
                    result = Visit(listNode);
                    if (listNode.Entries.Count > 0 && listNode.Entries[0].Pipeline.Commands.Count > 0)
                    {
                        var firstCmd = listNode.Entries[0].Pipeline.Commands[0];
                        if (firstCmd is LetNode letNode)
                        {
                            _env.Exports[letNode.VariableName] = _env.GetAurValue(letNode.VariableName);
                        }
                        else if (firstCmd is ConstNode constNode)
                        {
                            _env.Exports[constNode.VariableName] = _env.GetAurValue(constNode.VariableName);
                        }
                        else if (firstCmd is AssignmentNode assign)
                        {
                            _env.Exports[assign.VariableName] = _env.GetAurValue(assign.VariableName);
                        }
                        else if (firstCmd is FunctionNode func)
                        {
                            _env.Exports[func.Name] = new Types.AurFunction(func, _env);
                        }
                    }
                }
                return result;
            }
            if (execCmd.Name == "import")
            {
                if (execCmd.Args.Count != 1) {
                    Console.Error.WriteLine("aursh: import requires exactly 1 argument (the path to the script)");
                    return 1;
                }
                
                string scriptPath = execCmd.Args[0].Trim('\"', '\'');
                string resolvedPath = AurShell.Utils.FileSystem.ResolvePath(scriptPath, _workingDirectory);
                if (!System.IO.File.Exists(resolvedPath)) {
                    throw new Exception($"Import failed: Cannot find module '{resolvedPath}'");
                }
                
                string scriptContent = System.IO.File.ReadAllText(resolvedPath);
                
                try
                {
                    AurShell.Utils.ScriptValidator.Validate(resolvedPath, scriptContent);
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine(ex.Message);
                    return 1;
                }

                // Execute in a new environment
                var childEnv = new ShellEnvironment();
                var lexer = new Lexer(scriptContent, childEnv);
                var tokens = lexer.Tokenize();
                var parser = new Parser(tokens);
                var ast = parser.Parse();
                
                var evaluator = new AstEvaluator(childEnv, _executor, _workingDirectory);
                if (ast is ListNode listAst) evaluator.Visit(listAst);
                
                var moduleObj = new Types.AurObject();
                foreach (var kvp in childEnv.Exports)
                {
                    moduleObj.Properties[kvp.Key] = kvp.Value;
                }
                
                _env.LastReturnValue = moduleObj;
                return 0;
            }

            // Phase 5.5: Check for Index Assignment (e.g. obj[0] = val or obj["key"] = val)
            var indexMatch = System.Text.RegularExpressions.Regex.Match(execCmd.Name, @"^([a-zA-Z0-9_]+)\[(.+)\]$");
            if (indexMatch.Success)
            {
                string objName = indexMatch.Groups[1].Value;
                string indexStr = indexMatch.Groups[2].Value.Trim('"', '\'');
                var methodArgs = execCmd.Args;

                if (methodArgs.Count >= 2 && methodArgs[0] == "=")
                {
                    var obj = _env.GetAurValue(objName);
                    if (obj != null)
                    {
                        var argsToExpand = methodArgs.Skip(1).ToList();
                        var expandedValArgs = new List<string>();
                        foreach (var arg in argsToExpand)
                            expandedValArgs.AddRange(WordExpander.ExpandWord(arg, _env));
                            
                        string valStr = string.Join(" ", expandedValArgs);
                        var parsedVal = AurValueParser.Parse(valStr);

                        if (obj is Types.AurList aurList)
                        {
                            if (int.TryParse(indexStr, out int idx) && idx >= 0 && idx < aurList.Values.Count)
                            {
                                aurList.Values[idx] = parsedVal;
                                return 0;
                            }
                            else if (idx == aurList.Values.Count)
                            {
                                aurList.Values.Add(parsedVal);
                                return 0;
                            }
                            throw new Exception($"Index '{indexStr}' is out of bounds for list '{objName}'");
                        }
                        else if (obj is Types.AurObject aurObj)
                        {
                            aurObj.Properties[indexStr] = parsedVal;
                            return 0;
                        }
                        throw new Exception($"Cannot assign index on non-list/object '{objName}'");
                    }
                }
            }

            // Phase 5: Check for Object Method Calls or Property Assignment (e.g. obj.method(), obj.prop = val)
            var methodMatch = System.Text.RegularExpressions.Regex.Match(execCmd.Name, @"^([a-zA-Z0-9_]+)\.([a-zA-Z0-9_]+)$");
            if (methodMatch.Success)
            {
                string objName = methodMatch.Groups[1].Value;
                string propOrMethodName = methodMatch.Groups[2].Value;

                var obj = _env.GetAurValue(objName);
                if (obj != null)
                {
                    var methodArgs = execCmd.Args;

                    // Property Assignment: obj.prop = value
                    if (methodArgs.Count >= 2 && methodArgs[0] == "=")
                    {
                        if (obj is Types.AurObject aurObj)
                        {
                            var argsToExpand = methodArgs.Skip(1).ToList();
                            var expandedValArgs = new List<string>();
                            foreach (var arg in argsToExpand)
                                expandedValArgs.AddRange(WordExpander.ExpandWord(arg, _env));
                            
                            string valStr = string.Join(" ", expandedValArgs);
                            aurObj.Properties[propOrMethodName] = AurValueParser.Parse(valStr);
                            return 0;
                        }
                        throw new Exception($"Cannot assign property '{propOrMethodName}' on non-object '{objName}'");
                    }

                    // Method Call: obj.method(...)
                    var args = new List<Types.AurValue>();
                    if (methodArgs.Count >= 2 && methodArgs[0] == "(" && methodArgs[methodArgs.Count - 1] == ")")
                    {
                        for (int i = 1; i < methodArgs.Count - 1; i++)
                        {
                            if (methodArgs[i] != ",")
                                args.Add(new Types.AurString(string.Join(" ", WordExpander.ExpandWord(methodArgs[i], _env))));
                        }
                    }
                    var ret = obj.CallMethod(propOrMethodName, args);
                    _env.LastReturnValue = ret;
                    return 0; // Success
                }
                else
                {
                    throw new Exception($"Object '{objName}' not found.");
                }
            }

            // Phase 6: Check for JavaScript-style function calls (e.g. funcName(arg1, arg2))
            var funcCallMatch = System.Text.RegularExpressions.Regex.Match(execCmd.Name, @"^([a-zA-Z0-9_]+)\((.*)\)$");
            if (funcCallMatch.Success)
            {
                string funcName = funcCallMatch.Groups[1].Value;
                string argsStr = funcCallMatch.Groups[2].Value;

                var fBody = _env.GetFunction(funcName);
                if (fBody != null)
                {
                    return ExecuteWithRedirections(execCmd, () =>
                    {
                        var args = new List<string>();
                        if (!string.IsNullOrWhiteSpace(argsStr))
                        {
                            foreach (var arg in argsStr.Split(','))
                                args.Add(string.Join(" ", WordExpander.ExpandWord(arg.Trim(), _env)));
                        }

                        _env.PushFrame(new StackFrame(funcName, execCmd.Line, execCmd.Column, FrameType.Function));
                        _env.PushScope();
                        _env.PushPositionalArguments(args);

                        int exitCode = 1;
                        try
                        {
                            exitCode = Visit(fBody);
                        }
                        catch (Exception ex)
                        {
                            Console.Error.WriteLine($"aursh: error in function '{funcName}': {ex.Message}");
                            _env.PrintCallStack();
                        }
                        finally
                        {
                            _env.PopPositionalArguments();
                            _env.PopScope();
                            _env.PopFrame();
                            _returnRequested = false;
                        }

                        // If LastReturnValue is set, print it so $(...) captures it.
                        if (_env.LastReturnValue != null)
                        {
                            string retStr = _env.LastReturnValue.ToString();
                            if (!string.IsNullOrEmpty(retStr))
                            {
                                BuiltinCommands.WriteOut(retStr);
                            }
                        }

                        return exitCode;
                    });
                }
            }

            // Before executing an external command or builtin, check if it's a user-defined function.
            if (!string.IsNullOrEmpty(execCmd.Name))
            {
                var funcBody = _env.GetFunction(execCmd.Name);
                if (funcBody != null)
                {
                    return ExecuteWithRedirections(execCmd, () =>
                    {
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
        if (node.IsExported)
        {
            var funcObj = new Types.AurFunction(node, _env);
            _env.Exports[node.Name] = funcObj;
        }
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

    private int Visit(LetNode node)
    {
        var val = node.Expression != null ? EvaluateExpression(node.Expression) : new Types.AurString("");
        _env.SetLocalAurValue(node.VariableName, val);
        if (node.IsExported)
        {
            _env.Exports[node.VariableName] = val;
        }
        return 0;
    }

    private int Visit(ConstNode node)
    {
        var val = node.Expression != null ? EvaluateExpression(node.Expression) : new Types.AurString("");
        _env.SetLocalAurValue(node.VariableName, val);
        _env.MarkReadonly(node.VariableName);
        if (node.IsExported)
        {
            _env.Exports[node.VariableName] = val;
        }
        return 0;
    }

    private int Visit(AST.ExpressionStatementNode node)
    {
        var val = EvaluateExpression(node.Expression);
        
        // If there's an OutStream and the expression is not an assignment, output it
        // Actually, for a top-level expression, we should only output if it's evaluated in a context that captures it, 
        // or just let it set _env.LastReturnValue
        _env.LastReturnValue = val;
        
        if (!(node.Expression is AST.AssignmentExpressionNode) && OutStream != null)
        {
            var strVal = val.ToString();
            if (!string.IsNullOrEmpty(strVal))
            {
                var writer = new StreamWriter(OutStream, System.Text.Encoding.UTF8, leaveOpen: true) { AutoFlush = true };
                writer.WriteLine(strVal);
            }
        }
        return 0;
    }

    private int Visit(TryCatchNode node)
    {
        try 
        {
            return Visit(node.TryBlock);
        }
        catch (Exception ex)
        {
            _env.PushScope(); // new lexical block for catch
            _env.SetLocalAurValue(node.CatchVariable, new Types.AurString(ex.Message));
            int code = Visit(node.CatchBlock);
            _env.PopScope();
            return code;
        }
    }

    private Types.AurValue EvaluateExpression(AST.ExpressionNode expr)
    {
        if (expr is AST.LiteralExpressionNode lit)
        {
            return lit.Value ?? new Types.AurString("");
        }
        
        if (expr is AST.IdentifierExpressionNode id)
        {
            if (id.Name.Contains("$") || id.Name.Contains("~") || id.Name.Contains("\\"))
            {
                var expanded = WordExpander.ExpandWord(id.Name, _env);
                if (expanded.Count > 0)
                {
                    return new Types.AurString(string.Join(" ", expanded));
                }
                return new Types.AurString("");
            }

            var val = _env.GetAurValue(id.Name);
            if (val != null) return val;

            if (id.Name == "true" || id.Name == "false")
                return new Types.AurString(id.Name);

            return new Types.AurString("");
        }

        if (expr is AST.BinaryExpressionNode bin)
        {
            var left = EvaluateExpression(bin.Left);
            var right = EvaluateExpression(bin.Right);

            // Simple math if both are ints
            if (left is Types.AurInt lInt && right is Types.AurInt rInt)
            {
                return bin.Operator switch
                {
                    AST.BinaryOperator.Add => new Types.AurInt(lInt.Value + rInt.Value),
                    AST.BinaryOperator.Subtract => new Types.AurInt(lInt.Value - rInt.Value),
                    AST.BinaryOperator.Multiply => new Types.AurInt(lInt.Value * rInt.Value),
                    AST.BinaryOperator.Divide => new Types.AurInt(lInt.Value / rInt.Value),
                    AST.BinaryOperator.Equal => new Types.AurString(lInt.Value == rInt.Value ? "true" : "false"),
                    AST.BinaryOperator.NotEqual => new Types.AurString(lInt.Value != rInt.Value ? "true" : "false"),
                    AST.BinaryOperator.LessThan => new Types.AurString(lInt.Value < rInt.Value ? "true" : "false"),
                    AST.BinaryOperator.GreaterThan => new Types.AurString(lInt.Value > rInt.Value ? "true" : "false"),
                    AST.BinaryOperator.LessThanOrEqual => new Types.AurString(lInt.Value <= rInt.Value ? "true" : "false"),
                    AST.BinaryOperator.GreaterThanOrEqual => new Types.AurString(lInt.Value >= rInt.Value ? "true" : "false"),
                    _ => throw new Exception($"Unknown operator {bin.Operator} on ints")
                };
            }
            
            // String concatenation
            if (bin.Operator == AST.BinaryOperator.Add)
            {
                return new Types.AurString(left.ToString() + right.ToString());
            }
            
            if (bin.Operator == AST.BinaryOperator.Equal) return new Types.AurString(left.ToString() == right.ToString() ? "true" : "false");
            if (bin.Operator == AST.BinaryOperator.NotEqual) return new Types.AurString(left.ToString() != right.ToString() ? "true" : "false");

            throw new Exception($"Unsupported operator {bin.Operator} between {left} and {right}");
        }

        if (expr is AST.AssignmentExpressionNode assign)
        {
            if (assign.Left is AST.IdentifierExpressionNode idAssign)
            {
                var val = EvaluateExpression(assign.Right);
                _env.Set(idAssign.Name, val.ToString());
                // Actually it should use SetLocalAurValue if we want to store objects!
                _env.SetLocalAurValue(idAssign.Name, val);
                return val;
            }
            if (assign.Left is AST.MemberExpressionNode memAssign)
            {
                var objVal = EvaluateExpression(memAssign.Object);
                var rightVal = EvaluateExpression(assign.Right);
                if (objVal is Types.AurObject obj)
                {
                    obj.Properties[memAssign.PropertyName] = rightVal;
                    return rightVal;
                }
                throw new Exception("Cannot assign to property of non-object");
            }
            if (assign.Left is AST.IndexExpressionNode idxAssign)
            {
                var objVal = EvaluateExpression(idxAssign.Object);
                var idxVal = EvaluateExpression(idxAssign.Index);
                var rightVal = EvaluateExpression(assign.Right);
                
                if (objVal is Types.AurList list)
                {
                    if (int.TryParse(idxVal.ToString(), out int i))
                    {
                        if (i >= 0 && i < list.Values.Count) list.Values[i] = rightVal;
                        else if (i == list.Values.Count) list.Values.Add(rightVal);
                        else throw new Exception("Index out of bounds");
                        return rightVal;
                    }
                }
                else if (objVal is Types.AurObject obj)
                {
                    obj.Properties[idxVal.ToString()!] = rightVal;
                    return rightVal;
                }
                throw new Exception("Cannot index into non-list/object");
            }
            throw new Exception("Invalid assignment target");
        }

        if (expr is AST.MemberExpressionNode member)
        {
            var objVal = EvaluateExpression(member.Object);
            if (objVal is Types.AurObject obj)
            {
                if (obj.Properties.TryGetValue(member.PropertyName, out var propVal))
                    return propVal;
            }
            return new Types.AurString("");
        }

        if (expr is AST.IndexExpressionNode idx)
        {
            var objVal = EvaluateExpression(idx.Object);
            var idxVal = EvaluateExpression(idx.Index);
            
            if (objVal is Types.AurList list)
            {
                if (int.TryParse(idxVal.ToString(), out int i) && i >= 0 && i < list.Values.Count)
                {
                    return list.Values[i];
                }
            }
            else if (objVal is Types.AurObject obj)
            {
                if (obj.Properties.TryGetValue(idxVal.ToString()!, out var propVal))
                    return propVal;
            }
            return new Types.AurString("");
        }

        if (expr is AST.CallExpressionNode call)
        {
            if (call.Callee is AST.MemberExpressionNode methodCall)
            {
                var objVal = EvaluateExpression(methodCall.Object);
                if (objVal != null)
                {
                    var args = new List<Types.AurValue>();
                    foreach (var a in call.Arguments) args.Add(EvaluateExpression(a));
                    return objVal.CallMethod(methodCall.PropertyName, args);
                }
                throw new Exception($"Cannot call method {methodCall.PropertyName} on null");
            }
            else if (call.Callee is AST.IdentifierExpressionNode funcId)
            {
                if (funcId.Name == "print" || funcId.Name == "echo")
                {
                    var argsStrings = new List<string>();
                    foreach (var a in call.Arguments) argsStrings.Add(EvaluateExpression(a).ToString() ?? "");
                    var output = string.Join(" ", argsStrings);
                    if (OutStream != null)
                    {
                        var writer = new StreamWriter(OutStream, System.Text.Encoding.UTF8, leaveOpen: true) { AutoFlush = true };
                        writer.WriteLine(output);
                    }
                    else
                    {
                        Console.WriteLine(output);
                    }
                    return new Types.AurString(output);
                }

                if (funcId.Name == "import")
                {
                    if (call.Arguments.Count != 1)
                        throw new Exception("import requires exactly 1 argument (the path to the module)");
                    
                    string scriptPath = EvaluateExpression(call.Arguments[0]).ToString()!.Trim('\"', '\'');
                    string resolvedPath = AurShell.Utils.FileSystem.ResolvePath(scriptPath, _workingDirectory);
                    if (!System.IO.File.Exists(resolvedPath)) {
                        throw new Exception($"Import failed: Cannot find module '{resolvedPath}'");
                    }
                    
                    string scriptContent = System.IO.File.ReadAllText(resolvedPath);
                    
                    try
                    {
                        AurShell.Utils.ScriptValidator.Validate(resolvedPath, scriptContent);
                    }
                    catch (Exception ex)
                    {
                        Console.Error.WriteLine(ex.Message);
                        return new Types.AurString("1");
                    }
                    
                    // Execute in a new environment
                    var childEnv = new ShellEnvironment();
                    var lexer = new Lexer(scriptContent, childEnv);
                    var tokens = lexer.Tokenize();
                    var parser = new Parser(tokens);
                    var ast = parser.Parse();
                    
                    var evaluator = new AstEvaluator(childEnv, _executor, _workingDirectory);
                    if (ast is ListNode listAst) evaluator.Visit(listAst);
                    
                    var moduleObj = new Types.AurObject();
                    foreach (var kvp in childEnv.Exports)
                    {
                        moduleObj.Properties[kvp.Key] = kvp.Value;
                    }
                    
                    return moduleObj;
                }

                var funcBody = _env.GetFunction(funcId.Name);
                if (funcBody != null)
                {
                    var args = new List<string>();
                    foreach (var a in call.Arguments) args.Add(EvaluateExpression(a).ToString()!);

                    _env.PushFrame(new StackFrame(funcId.Name, 0, 0, FrameType.Function));
                    _env.PushScope();
                    _env.PushPositionalArguments(args);

                    try
                    {
                        Visit(funcBody);
                    }
                    finally
                    {
                        _env.PopPositionalArguments();
                        _env.PopScope();
                        _env.PopFrame();
                        _returnRequested = false;
                    }

                    return _env.LastReturnValue ?? new Types.AurString("");
                }
                throw new Exception($"Function {funcId.Name} not found");
            }
            throw new Exception("Invalid function call target");
        }

        if (expr is AST.ObjectExpressionNode objNode)
        {
            var aurObj = new Types.AurObject();
            foreach (var kvp in objNode.Properties)
            {
                aurObj.Properties[kvp.Key] = EvaluateExpression(kvp.Value);
            }
            return aurObj;
        }
        
        if (expr is AST.ArrayExpressionNode arrNode)
        {
            var aurList = new Types.AurList();
            foreach (var element in arrNode.Elements)
            {
                aurList.Values.Add(EvaluateExpression(element));
            }
            return aurList;
        }

        return new Types.AurString("");
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
