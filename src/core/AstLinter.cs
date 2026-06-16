using System;
using System.Collections.Generic;

namespace AurShell.Core;

public class LinterWarning
{
    public string Message { get; }
    public LinterWarning(string message) => Message = message;
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
                _warnings.Add(new LinterWarning($"'{cmd.Name}' used outside of a loop context."));
            }
        }
        else if (cmd.Name == "return")
        {
            if (_functionDepth == 0)
            {
                _warnings.Add(new LinterWarning($"'return' used outside of a function context."));
            }
        }

        // Warn on unquoted variables that might undergo word splitting
        foreach (var arg in cmd.Args)
        {
            if (arg.Contains("$") && !arg.StartsWith("\"") && !arg.StartsWith("'"))
            {
                _warnings.Add(new LinterWarning($"Unquoted variable expansion in argument '{arg}' may be subject to word splitting."));
            }
        }
    }
}
