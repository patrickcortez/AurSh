using System;
using System.Collections.Generic;

namespace AurShell.Core;

public enum Severity
{
    Warning,
    Error,
    Info
}

public class LinterWarning
{
    public int Line { get; }
    public int Column { get; }
    public string Message { get; }
    public Severity Severity { get; }

    public LinterWarning(int line, int column, string message, Severity severity = Severity.Warning)
    {
        Line = line;
        Column = column;
        Message = message;
        Severity = severity;
    }

    public override string ToString()
    {
        return $"[Line {Line}, Col {Column}] {Severity}: {Message}";
    }
}

public class AstLinter
{
    private int _loopDepth = 0;
    private int _functionDepth = 0;
    private readonly List<LinterWarning> _warnings = new();

    public IReadOnlyList<LinterWarning> Analyze(ListNode root)
    {
        _warnings.Clear();
        _loopDepth = 0;
        _functionDepth = 0;
        Visit(root);
        return _warnings;
    }

    private void Visit(ListNode node)
    {
        foreach (var entry in node.Entries)
        {
            Visit(entry.Pipeline);
        }
    }

    private void Visit(PipelineNode node)
    {
        foreach (var cmd in node.Commands)
        {
            Visit(cmd);
        }
    }

    private void Visit(ICommandNode node)
    {
        switch (node)
        {
            case SimpleCommandNode scn:
                AnalyzeSimpleCommand(scn);
                break;
            case IfNode inod:
                Visit(inod.Condition);
                Visit(inod.ThenBlock);
                foreach (var elif in inod.ElifBlocks)
                {
                    Visit(elif.condition);
                    Visit(elif.block);
                }
                if (inod.ElseBlock != null) Visit(inod.ElseBlock);
                break;
            case WhileNode wn:
                Visit(wn.Condition);
                _loopDepth++;
                Visit(wn.Body);
                _loopDepth--;
                break;
            case UntilNode un:
                Visit(un.Condition);
                _loopDepth++;
                Visit(un.Body);
                _loopDepth--;
                break;
            case ForNode fn:
                _loopDepth++;
                Visit(fn.Body);
                _loopDepth--;
                break;
            case BlockNode bn:
                Visit(bn.Body);
                break;
            case SubshellNode ssn:
                Visit(ssn.Body);
                break;
            case FunctionNode fnod:
                _functionDepth++;
                Visit(fnod.Body);
                _functionDepth--;
                break;
        }
    }

    private void AnalyzeSimpleCommand(SimpleCommandNode cmd)
    {
        if (cmd.Name == "break" || cmd.Name == "continue")
        {
            if (_loopDepth == 0)
            {
                _warnings.Add(new LinterWarning(cmd.Line, cmd.Column, $"'{cmd.Name}' used outside of a loop context.", Severity.Error));
            }
        }
        else if (cmd.Name == "return")
        {
            if (_functionDepth == 0)
            {
                _warnings.Add(new LinterWarning(cmd.Line, cmd.Column, $"'return' used outside of a function context.", Severity.Error));
            }
        }
        else if (cmd.Name == "[")
        {
            if (cmd.Args.Count == 0 || !cmd.Args[^1].EndsWith("]"))
            {
                _warnings.Add(new LinterWarning(cmd.Line, cmd.Column, $"The '[' command requires a closing ']'.", Severity.Error));
            }
            else if (cmd.Args[^1] != "]")
            {
                _warnings.Add(new LinterWarning(cmd.Line, cmd.Column, $"Missing space before closing ']'. The '[' command requires exactly ']' as the final argument, not '{cmd.Args[^1]}'.", Severity.Error));
            }
        }

        // Warn on unquoted variables that might undergo word splitting
        for (int i = 0; i < cmd.Args.Count; i++)
        {
            string arg = cmd.Args[i];
            string rawArg = i < cmd.RawExpandedArgs.Count ? cmd.RawExpandedArgs[i] : arg;
            string trimmedRaw = rawArg.TrimStart();

            if (rawArg.Contains("$") && !trimmedRaw.StartsWith("\"") && !trimmedRaw.StartsWith("'"))
            {
                _warnings.Add(new LinterWarning(cmd.Line, cmd.Column, $"Unquoted variable expansion in argument '{rawArg}' may be subject to word splitting.", Severity.Warning));
            }
        }
    }
}
