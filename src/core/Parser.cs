namespace AurShell.Core;

public enum RedirectType
{
    Out,
    Append,
    In,
    Err,
    ErrAppend,
    ErrToOut,
    HereDoc,
    HereString
}

public class Redirection
{
    public RedirectType Type { get; }
    public string Target { get; }

    public Redirection(RedirectType type, string target)
    {
        Type = type;
        Target = target;
    }
}

public interface ICommandNode
{
    int Line { get; set; }
    int Column { get; set; }
    List<Redirection> Redirections { get; }
}

public class SimpleCommandNode : ICommandNode
{
    public int Line { get; set; }
    public int Column { get; set; }
    public string Name { get; set; } = "";
    public string RawExpandedName { get; set; } = "";
    public List<AssignmentNode> PrefixAssignments { get; } = new();
    public List<string> Args { get; } = new();
    public List<string> RawExpandedArgs { get; } = new();
    public List<Redirection> Redirections { get; } = new();

    public string[] AllArgs
    {
        get
        {
            var all = new List<string>();
            if (!string.IsNullOrEmpty(Name))
                all.Add(Name);
            all.AddRange(Args);
            return all.ToArray();
        }
    }
}

public class IfNode : ICommandNode
{
    public int Line { get; set; }
    public int Column { get; set; }
    public ListNode Condition { get; set; } = new();
    public ListNode ThenBlock { get; set; } = new();
    public List<(ListNode condition, ListNode block)> ElifBlocks { get; } = new();
    public ListNode? ElseBlock { get; set; }
    public List<Redirection> Redirections { get; } = new();
}

public class WhileNode : ICommandNode
{
    public int Line { get; set; }
    public int Column { get; set; }
    public ListNode Condition { get; set; } = new();
    public ListNode Body { get; set; } = new();
    public List<Redirection> Redirections { get; } = new();
}

public class UntilNode : ICommandNode
{
    public int Line { get; set; }
    public int Column { get; set; }
    public ListNode Condition { get; set; } = new();
    public ListNode Body { get; set; } = new();
    public List<Redirection> Redirections { get; } = new();
}

public class ForNode : ICommandNode
{
    public int Line { get; set; }
    public int Column { get; set; }
    public string VariableName { get; set; } = "";
    public List<string> IteratorValues { get; } = new();
    public ListNode Body { get; set; } = new();
    public List<Redirection> Redirections { get; } = new();
}

public class CaseNode : ICommandNode
{
    public int Line { get; set; }
    public int Column { get; set; }
    public string Value { get; set; } = "";
    public List<(List<string> Patterns, ListNode Body)> Cases { get; } = new();
    public List<Redirection> Redirections { get; } = new();
}

public class BlockNode : ICommandNode
{
    public int Line { get; set; }
    public int Column { get; set; }
    public ListNode Body { get; set; } = new();
    public List<Redirection> Redirections { get; } = new();
}

public class SubshellNode : ICommandNode
{
    public int Line { get; set; }
    public int Column { get; set; }
    public ListNode Body { get; set; } = new();
    public List<Redirection> Redirections { get; } = new();
}

public class FunctionNode : ICommandNode
{
    public int Line { get; set; }
    public int Column { get; set; }
    public string Name { get; set; } = "";
    public BlockNode Body { get; set; } = new();
    public bool IsExported { get; set; } = false;
    public List<Redirection> Redirections { get; } = new();
}

public class AssignmentNode : ICommandNode
{
    public int Line { get; set; }
    public int Column { get; set; }
    public string VariableName { get; set; } = "";
    public string Value { get; set; } = "";
    public string RawExpandedValue { get; set; } = "";
    public List<Redirection> Redirections { get; } = new();
}

public class LetNode : ICommandNode
{
    public int Line { get; set; }
    public int Column { get; set; }
    public string VariableName { get; set; } = "";
    public AST.ExpressionNode? Expression { get; set; }
    public bool IsExported { get; set; } = false;
    public List<Redirection> Redirections { get; } = new();
}

public class ConstNode : ICommandNode
{
    public int Line { get; set; }
    public int Column { get; set; }
    public string VariableName { get; set; } = "";
    public AST.ExpressionNode? Expression { get; set; }
    public bool IsExported { get; set; } = false;
    public List<Redirection> Redirections { get; } = new();
}

public class TryCatchNode : ICommandNode
{
    public int Line { get; set; }
    public int Column { get; set; }
    public BlockNode TryBlock { get; set; } = new();
    public string CatchVariable { get; set; } = "err";
    public BlockNode CatchBlock { get; set; } = new();
    public List<Redirection> Redirections { get; } = new();
}

public class ArrayAssignmentNode : ICommandNode
{
    public int Line { get; set; }
    public int Column { get; set; }
    public string VariableName { get; set; } = "";
    public List<string> Values { get; } = new();
    public List<Redirection> Redirections { get; } = new();
}

public enum ListOperator
{
    Sequential,
    And,
    Or
}

public class PipelineNode
{
    public int Line { get; set; }
    public int Column { get; set; }
    public List<ICommandNode> Commands { get; } = new();
    public bool Background { get; set; }
}

public class ListEntry
{
    public PipelineNode Pipeline { get; }
    public ListOperator Operator { get; }

    public ListEntry(PipelineNode pipeline, ListOperator op)
    {
        Pipeline = pipeline;
        Operator = op;
    }
}

public class ListNode
{
    public int Line { get; set; }
    public int Column { get; set; }
    public List<ListEntry> Entries { get; } = new();
}

public class Parser
{
    private readonly List<Token> _tokens;
    private int _pos;

    public Parser(List<Token> tokens)
    {
        _tokens = tokens;
        _pos = 0;
    }

    private T InitNode<T>(T node)
    {
        var current = Current;
        if (current != null)
        {
            if (node is ICommandNode cmdNode)
            {
                cmdNode.Line = current.Line;
                cmdNode.Column = current.Column;
            }
            else if (node is ListNode listNode)
            {
                listNode.Line = current.Line;
                listNode.Column = current.Column;
            }
            else if (node is PipelineNode pipeNode)
            {
                pipeNode.Line = current.Line;
                pipeNode.Column = current.Column;
            }
        }
        return node;
    }

    public ListNode Parse()
    {
        return ParseListUntilKeyword();
    }

    private ListNode ParseListUntilKeyword(params string[] stopWords)
    {
        var list = InitNode(new ListNode());
        SkipNewlines();

        while (Current.Type != TokenType.EOF)
        {
            if (!Current.WasQuoted && stopWords.Contains(Current.Value))
            {
                break;
            }

            var pipeline = ParsePipeline();
            if (pipeline == null || pipeline.Commands.Count == 0)
            {
                if (Current.Type != TokenType.EOF && !stopWords.Contains(Current.Value))
                {
                    Console.Error.WriteLine($"aursh: syntax error near unexpected token `{Current.Value}`");
                    Advance(); // prevent infinite loops
                    break;
                }
                SkipNewlines();
                continue;
            }

            ListOperator op = ListOperator.Sequential;

            if (Current.Type == TokenType.And)
            {
                op = ListOperator.And;
                Advance();
            }
            else if (Current.Type == TokenType.Or)
            {
                op = ListOperator.Or;
                Advance();
            }
            else if (Current.Type == TokenType.Semicolon)
            {
                op = ListOperator.Sequential;
                Advance();
            }
            else if (Current.Type == TokenType.Newline)
            {
                op = ListOperator.Sequential;
                Advance();
            }

            list.Entries.Add(new ListEntry(pipeline, op));
            SkipNewlines();
        }

        return list;
    }

    private PipelineNode? ParsePipeline()
    {
        var pipeline = InitNode(new PipelineNode());

        var cmd = ParseCommand();
        if (cmd == null)
            return null;

        pipeline.Commands.Add(cmd);

        while (Current.Type == TokenType.Pipe)
        {
            Advance();
            SkipNewlines();

            var nextCmd = ParseCommand();
            if (nextCmd == null)
                break;

            pipeline.Commands.Add(nextCmd);
        }

        if (Current.Type == TokenType.Background)
        {
            pipeline.Background = true;
            Advance();
        }

        return pipeline;
    }

    private ICommandNode? ParseCommand()
    {
        if (Current.Type == TokenType.LeftParen)
            return ParseSubshell();

        if (Current.Type == TokenType.LeftBrace || (Current.Type == TokenType.Word && Current.Value == "{"))
            return ParseBlock();

        if (Current.Type == TokenType.Word && !Current.WasQuoted)
        {
            if (Current.Value == "if") return ParseIf();
            if (Current.Value == "while") return ParseWhile();
            if (Current.Value == "until") return ParseUntil();
            if (Current.Value == "for") return ParseFor();
            if (Current.Value == "case") return ParseCase();
            if (Current.Value == "let") return ParseLet();
            if (Current.Value == "const") return ParseConst();
            if (Current.Value == "try") return ParseTryCatch();

            if (Current.Value == "function" && Next.Type == TokenType.Word)
                return ParseFunction();
                
            if (Current.Value == "export")
            {
                if (Next.Type == TokenType.Word && Next.Value == "function")
                {
                    Advance(); // consume 'export'
                    var funcNode = ParseFunction();
                    if (funcNode != null) funcNode.IsExported = true;
                    return funcNode;
                }
                else if (Next.Type == TokenType.Word && Next.Value == "let")
                {
                    Advance(); // consume 'export'
                    var letNode = ParseLet();
                    if (letNode != null) letNode.IsExported = true;
                    return letNode;
                }
                else if (Next.Type == TokenType.Word && Next.Value == "const")
                {
                    Advance(); // consume 'export'
                    var constNode = ParseConst();
                    if (constNode != null) constNode.IsExported = true;
                    return constNode;
                }
            }
        }

        if (Current.Type == TokenType.Word && Next.Type == TokenType.LeftParen && LookAhead(2).Type == TokenType.RightParen)
        {
            var la3 = LookAhead(3);
            var la4 = LookAhead(4);
            bool hasBrace = la3.Type == TokenType.LeftBrace || (la3.Type == TokenType.Word && la3.Value == "{") ||
                            (la3.Type == TokenType.Newline && (la4.Type == TokenType.LeftBrace || (la4.Type == TokenType.Word && la4.Value == "{")));
            
            if (hasBrace)
            {
                return ParseFunction();
            }
        }

        if (Current.Type == TokenType.Word && Current.Value.Contains("="))
        {
            // Only reject if the variable name part was quoted (e.g., "v1"=Hello)
            string raw = Current.RawExpandedValue ?? Current.Value;
            int rawEq = raw.IndexOf('=');
            if (rawEq > 0 && !raw.Substring(0, rawEq).Contains("\"") && !raw.Substring(0, rawEq).Contains("'"))
            {
                if (IsValidAssignment(Current.Value))
                {
                    if (StartsAssignmentPrefixedCommand())
                        return ParseSimpleCommand();

                    int equalsIdx = Current.Value.IndexOf('=');
                    string name = Current.Value.Substring(0, equalsIdx);
                    if (Current.Value.EndsWith("=") && Next.Type == TokenType.LeftParen)
                        return ParseArrayAssignment(name);
                    else
                        return ParseAssignment(name);
                }
            }
        }

        int startPos = _pos;
        var exprParser = new AST.ExpressionParser(_tokens, _pos);
        try
        {
            var expr = exprParser.ParseExpression();
            if (expr != null)
            {
                int nextPos = exprParser.GetCurrentPosition();
                var nextToken = nextPos < _tokens.Count ? _tokens[nextPos] : new Token(TokenType.EOF, "", 0, 0);

                if (nextToken.Type == TokenType.Newline || nextToken.Type == TokenType.Semicolon || nextToken.Type == TokenType.EOF || nextToken.Type == TokenType.Pipe || IsRedirect(nextToken.Type))
                {
                    if (expr is AST.AssignmentExpressionNode || expr is AST.CallExpressionNode || expr is AST.MemberExpressionNode || expr is AST.IndexExpressionNode)
                    {
                        _pos = nextPos;
                        var exprNode = InitNode(new AST.ExpressionStatementNode(expr));
                        while (IsRedirect(Current.Type)) ParseRedirection(exprNode);
                        return exprNode;
                    }
                }
            }
        }
        catch
        {
            // fallback to SimpleCommand
        }
        
        _pos = startPos; // reset in case expression partially parsed but failed
        return ParseSimpleCommand();
    }

    private bool IsSimpleCommandArg(TokenType type)
    {
        return type == TokenType.Word || type == TokenType.Plus || type == TokenType.Minus || 
               type == TokenType.Multiply || type == TokenType.Divide || type == TokenType.Dot || 
               type == TokenType.Comma || type == TokenType.Colon || type == TokenType.Equal || 
               type == TokenType.NotEqual || type == TokenType.LessThan || type == TokenType.GreaterThan || 
               type == TokenType.LessThanOrEqual || type == TokenType.GreaterThanOrEqual || 
               type == TokenType.Assign || type == TokenType.Not || type == TokenType.LeftBracket || 
               type == TokenType.RightBracket || type == TokenType.LeftBrace || type == TokenType.RightBrace ||
               type == TokenType.LeftParen || type == TokenType.RightParen;
    }

    private SimpleCommandNode? ParseSimpleCommand()
    {
        var cmd = InitNode(new SimpleCommandNode());
        bool hasContent = false;
        var argBuilder = new System.Text.StringBuilder();
        var rawArgBuilder = new System.Text.StringBuilder();

        while (IsSimpleCommandArg(Current.Type))
        {
            if (!hasContent && Current.Type == TokenType.Word && IsValidAssignment(Current.Value) && StartsAssignmentPrefixedCommand())
            {
                cmd.PrefixAssignments.Add(CreateAssignmentFromCurrent());
                Advance();
                continue;
            }

            if (Current.HasLeadingSpace && argBuilder.Length > 0)
            {
                if (!hasContent)
                {
                    cmd.Name = argBuilder.ToString();
                    cmd.RawExpandedName = rawArgBuilder.ToString();
                    hasContent = true;
                }
                else
                {
                    cmd.Args.Add(argBuilder.ToString());
                    cmd.RawExpandedArgs.Add(rawArgBuilder.ToString());
                }
                argBuilder.Clear();
                rawArgBuilder.Clear();
            }

            argBuilder.Append(Current.Value);
            rawArgBuilder.Append(Current.RawExpandedValue);
            Advance();

            while (IsRedirect(Current.Type))
            {
                if (argBuilder.Length > 0)
                {
                    if (!hasContent) { cmd.Name = argBuilder.ToString(); cmd.RawExpandedName = rawArgBuilder.ToString(); hasContent = true; }
                    else { cmd.Args.Add(argBuilder.ToString()); cmd.RawExpandedArgs.Add(rawArgBuilder.ToString()); }
                    argBuilder.Clear(); rawArgBuilder.Clear();
                }
                ParseRedirection(cmd);
                hasContent = true;
            }
        }

        if (argBuilder.Length > 0)
        {
            if (!hasContent) { cmd.Name = argBuilder.ToString(); cmd.RawExpandedName = rawArgBuilder.ToString(); hasContent = true; }
            else { cmd.Args.Add(argBuilder.ToString()); cmd.RawExpandedArgs.Add(rawArgBuilder.ToString()); }
        }

        while (IsRedirect(Current.Type))
        {
            ParseRedirection(cmd);
            hasContent = true;
        }

        return hasContent ? cmd : null;
    }

    private bool StartsAssignmentPrefixedCommand()
    {
        int lookahead = 0;
        while (LookAhead(lookahead).Type == TokenType.Word && IsValidAssignment(LookAhead(lookahead).Value))
            lookahead++;

        return LookAhead(lookahead).Type == TokenType.Word;
    }

    private AssignmentNode CreateAssignmentFromCurrent()
    {
        var node = InitNode(new AssignmentNode());
        int equalsIdx = Current.Value.IndexOf('=');
        node.VariableName = Current.Value.Substring(0, equalsIdx);
        node.Value = Current.Value.Substring(equalsIdx + 1);

        int rawEqualsIdx = Current.RawExpandedValue.IndexOf('=');
        node.RawExpandedValue = rawEqualsIdx >= 0
            ? Current.RawExpandedValue.Substring(rawEqualsIdx + 1)
            : node.Value;

        return node;
    }

    private LetNode? ParseLet()
    {
        var node = InitNode(new LetNode());
        Advance(); // consume 'let'

        if (Current.Type != TokenType.Word)
        {
            Console.Error.WriteLine($"aursh: let: missing variable name");
            return null;
        }

        string rawToken = Current.Value;
        if (rawToken.Contains("="))
        {
            int eqIdx = rawToken.IndexOf("=");
            node.VariableName = rawToken.Substring(0, eqIdx);
            
            // For now, if they attached it like x=5, we just parse the rest as string in ExpressionParser?
            // Actually, Lexer now spits out '=' as its own token if there are spaces, but if it's attached like let x=5, 
            // Lexer gives Word("x=5"). We should probably split the token or just leave it.
            // Wait, Lexer splits `=` into a TokenType.Equal if we added it as an operator!
            // Let's assume Lexer doesn't split it yet for words starting with a character?
            // If it DOES contain `=`, then we need to parse the rest. But since Lexer was updated to tokenize `=`, `x=5` should be `Word(x)`, `Equal`, `Word(5)`!
            // Ah, so Lexer doesn't output `x=5` as a single word anymore if it sees `=`? Wait, Lexer was updated to treat `=` as an operator.
        }
        
        node.VariableName = Current.Value;
        Advance();

        if (Current.Type == TokenType.Equal)
        {
            Advance(); // consume '='
        }
        else if (Current.Type == TokenType.Assign) // Just in case Lexer emitted Assign
        {
            Advance();
        }
        else if (Current.Value == "=")
        {
            Advance();
        }
        else
        {
            Console.Error.WriteLine($"aursh: let: expected '=' after '{node.VariableName}'");
            return null;
        }
        
        var exprParser = new AST.ExpressionParser(_tokens, _pos);
        var expr = exprParser.ParseExpression();
        if (expr != null)
        {
            node.Expression = expr;
            _pos = exprParser.GetCurrentPosition();
        }
        else
        {
            Console.Error.WriteLine($"aursh: let: expected expression after '='");
            return null;
        }

        while (IsRedirect(Current.Type)) ParseRedirection(node);
        return node;
    }

    private ConstNode? ParseConst()
    {
        var node = InitNode(new ConstNode());
        Advance(); // consume 'const'

        if (Current.Type != TokenType.Word)
        {
            Console.Error.WriteLine($"aursh: const: missing variable name");
            return null;
        }

        node.VariableName = Current.Value;
        Advance();

        if (Current.Type == TokenType.Equal)
        {
            Advance(); // consume '='
        }
        else if (Current.Type == TokenType.Assign)
        {
            Advance();
        }
        else if (Current.Value == "=")
        {
            Advance();
        }
        else
        {
            Console.Error.WriteLine($"aursh: const: expected '=' after '{node.VariableName}'");
            return null;
        }

        var exprParser = new AST.ExpressionParser(_tokens, _pos);
        var expr = exprParser.ParseExpression();
        if (expr != null)
        {
            node.Expression = expr;
            _pos = exprParser.GetCurrentPosition();
        }
        else
        {
            Console.Error.WriteLine($"aursh: const: expected expression after '='");
            return null;
        }

        while (IsRedirect(Current.Type)) ParseRedirection(node);
        return node;
    }

    private TryCatchNode? ParseTryCatch()
    {
        var node = InitNode(new TryCatchNode());
        Advance(); // consume 'try'

        if (Current.Type == TokenType.LeftBrace || (Current.Type == TokenType.Word && Current.Value == "{"))
        {
            node.TryBlock = ParseBlock()!;
        }
        else
        {
            Console.Error.WriteLine("aursh: try must be followed by a block '{ ... }'");
            return null;
        }

        ExpectKeyword("catch", node.Line, node.Column);

        if (Current.Type == TokenType.Word && Current.Value != "{")
        {
            node.CatchVariable = Current.Value;
            Advance();
        }

        if (Current.Type == TokenType.LeftBrace || (Current.Type == TokenType.Word && Current.Value == "{"))
        {
            node.CatchBlock = ParseBlock()!;
        }
        else
        {
            Console.Error.WriteLine("aursh: catch must be followed by a block '{ ... }'");
            return null;
        }

        while (IsRedirect(Current.Type)) ParseRedirection(node);
        return node;
    }

    private IfNode? ParseIf()
    {
        var node = InitNode(new IfNode());
        Advance(); // consume 'if'

        node.Condition = ParseListUntilKeyword("then");
        ExpectKeyword("then", node.Line, node.Column);

        node.ThenBlock = ParseListUntilKeyword("elif", "else", "fi");

        while (Current.Type == TokenType.Word && Current.Value == "elif" && !Current.WasQuoted)
        {
            Advance();
            var elifCond = ParseListUntilKeyword("then");
            ExpectKeyword("then", node.Line, node.Column);
            var elifBlock = ParseListUntilKeyword("elif", "else", "fi");
            node.ElifBlocks.Add((elifCond, elifBlock));
        }

        if (Current.Type == TokenType.Word && Current.Value == "else" && !Current.WasQuoted)
        {
            Advance();
            node.ElseBlock = ParseListUntilKeyword("fi");
        }

        ExpectKeyword("fi", node.Line, node.Column);

        while (IsRedirect(Current.Type)) ParseRedirection(node);
        return node;
    }

    private SubshellNode? ParseSubshell()
    {
        var node = InitNode(new SubshellNode());
        Advance(); // consume '('
        node.Body = ParseListUntilKeyword(")");
        ExpectToken(TokenType.RightParen, ")", node.Line, node.Column);
        while (IsRedirect(Current.Type)) ParseRedirection(node);
        return node;
    }

    private FunctionNode? ParseFunction(string? name = null)
    {
        var node = InitNode(new FunctionNode());

        if (name == null)
        {
            if (Current.Value == "function")
            {
                Advance(); // consume 'function'
                node.Name = Current.Value;
                Advance(); // consume name
                if (Current.Type == TokenType.LeftParen)
                {
                    Advance();
                    if (Current.Type == TokenType.RightParen) Advance();
                }
            }
            else
            {
                node.Name = Current.Value;
                Advance(); // consume name
                Advance(); // consume '('
                Advance(); // consume ')'
            }
        }
        else
        {
            node.Name = name;
        }

        SkipNewlines();

        if (Current.Type == TokenType.LeftBrace || (Current.Type == TokenType.Word && Current.Value == "{" && !Current.WasQuoted))
        {
            node.Body = ParseBlock() ?? new BlockNode();
        }

        while (IsRedirect(Current.Type)) ParseRedirection(node);
        return node;
    }

    private bool IsValidAssignment(string value)
    {
        int equalsIdx = value.IndexOf('=');
        if (equalsIdx <= 0) return false;
        string name = value.Substring(0, equalsIdx);
        if (name.Length == 0) return false;
        if (!char.IsLetter(name[0]) && name[0] != '_') return false;
        for (int i = 1; i < name.Length; i++)
        {
            if (!char.IsLetterOrDigit(name[i]) && name[i] != '_') return false;
        }
        return true;
    }

    private AssignmentNode? ParseAssignment(string name)
    {
        var node = InitNode(new AssignmentNode());
        int equalsIdx = Current.Value.IndexOf('=');
        node.VariableName = name;
        node.Value = Current.Value.Substring(equalsIdx + 1);
        node.RawExpandedValue = Current.RawExpandedValue.Substring(equalsIdx + 1); // rough approximation
        Advance();

        while (IsRedirect(Current.Type)) ParseRedirection(node);
        return node;
    }

    private ArrayAssignmentNode? ParseArrayAssignment(string name)
    {
        var node = InitNode(new ArrayAssignmentNode());
        node.VariableName = name;
        Advance(); // consume 'var='
        Advance(); // consume '('

        while (Current.Type != TokenType.EOF && Current.Type != TokenType.RightParen)
        {
            if (Current.Type == TokenType.Word)
            {
                node.Values.Add(Current.Value);
            }
            Advance();
        }

        if (Current.Type == TokenType.RightParen) Advance();
        while (IsRedirect(Current.Type)) ParseRedirection(node);
        return node;
    }

    private WhileNode? ParseWhile()
    {
        var node = InitNode(new WhileNode());
        Advance(); // consume 'while'
        node.Condition = ParseListUntilKeyword("do");
        ExpectKeyword("do", node.Line, node.Column);
        node.Body = ParseListUntilKeyword("done");
        ExpectKeyword("done", node.Line, node.Column);
        while (IsRedirect(Current.Type)) ParseRedirection(node);
        return node;
    }

    private UntilNode? ParseUntil()
    {
        var node = InitNode(new UntilNode());
        Advance(); // consume 'until'
        node.Condition = ParseListUntilKeyword("do");
        ExpectKeyword("do", node.Line, node.Column);
        node.Body = ParseListUntilKeyword("done");
        ExpectKeyword("done", node.Line, node.Column);
        while (IsRedirect(Current.Type)) ParseRedirection(node);
        return node;
    }

    private ForNode? ParseFor()
    {
        var node = InitNode(new ForNode());
        Advance(); // consume 'for'
        if (Current.Type == TokenType.Word)
        {
            node.VariableName = Current.Value;
            Advance();
        }

        SkipNewlines();

        if (Current.Type == TokenType.Word && Current.Value == "in" && !Current.WasQuoted)
        {
            Advance();
            while (Current.Type == TokenType.Word && !(Current.Value == "do" && !Current.WasQuoted) && Current.Type != TokenType.Newline && Current.Type != TokenType.Semicolon)
            {
                node.IteratorValues.Add(Current.RawExpandedValue);
                Advance();
            }
        }

        if (Current.Type == TokenType.Semicolon || Current.Type == TokenType.Newline)
            Advance();

        SkipNewlines();

        ExpectKeyword("do", node.Line, node.Column);
        node.Body = ParseListUntilKeyword("done");
        ExpectKeyword("done", node.Line, node.Column);
        while (IsRedirect(Current.Type)) ParseRedirection(node);
        return node;
    }

    private CaseNode? ParseCase()
    {
        var node = InitNode(new CaseNode());
        Advance(); // consume 'case'

        if (Current.Type == TokenType.Word)
        {
            node.Value = Current.RawExpandedValue;
            Advance();
        }

        SkipNewlines();
        if (Current.Type == TokenType.Word && Current.Value == "in" && !Current.WasQuoted) Advance();
        SkipNewlines();

        while (Current.Type != TokenType.EOF && !(Current.Type == TokenType.Word && Current.Value == "esac" && !Current.WasQuoted))
        {
            var patterns = new List<string>();
            while (Current.Type == TokenType.Word && Current.Value != "esac")
            {
                patterns.Add(Current.RawExpandedValue);
                Advance();
                if (Current.Type == TokenType.Pipe)
                    Advance();
                else
                    break;
            }

            if (patterns.Count > 0)
            {
                string last = patterns.Last();
                if (last.EndsWith(")"))
                    patterns[patterns.Count - 1] = last.Substring(0, last.Length - 1);
                else if (Current.Type == TokenType.RightParen || (Current.Type == TokenType.Word && Current.Value == ")"))
                    Advance();
            }

            var body = ParseListUntilKeyword(";;", "esac");
            node.Cases.Add((patterns, body));

            if (Current.Type == TokenType.DoubleSemicolon)
                Advance();
            else if (Current.Type == TokenType.Word && Current.Value == ";;" && !Current.WasQuoted)
                Advance();

            SkipNewlines();
        }

        ExpectKeyword("esac", node.Line, node.Column);
        while (IsRedirect(Current.Type)) ParseRedirection(node);
        return node;
    }

    private BlockNode? ParseBlock()
    {
        var node = InitNode(new BlockNode());
        Advance(); // consume '{'
        node.Body = ParseListUntilKeyword("}");
        
        if (Current.Type == TokenType.RightBrace || (Current.Type == TokenType.Word && Current.Value == "}"))
        {
            Advance();
        }
        else
        {
            ExpectKeyword("}", node.Line, node.Column);
        }
        while (IsRedirect(Current.Type)) ParseRedirection(node);
        return node;
    }

    private void ParseRedirection(ICommandNode cmd)
    {
        TokenType redirectType = Current.Type;
        Advance();

        string target = "";

        // 2>&1 does not require a target word
        if (redirectType != TokenType.RedirectErrToOut)
        {
            if (Current.Type != TokenType.Word)
            {
                Console.Error.WriteLine("aursh: syntax error near unexpected token");
                return;
            }

            target = Current.Value;
            Advance();

            if (redirectType == TokenType.HereDoc && Current.Type == TokenType.HereDocText)
            {
                target = Current.Value;
                Advance();
            }
        }

        RedirectType type = redirectType switch
        {
            TokenType.RedirectOut => RedirectType.Out,
            TokenType.RedirectAppend => RedirectType.Append,
            TokenType.RedirectIn => RedirectType.In,
            TokenType.RedirectErr => RedirectType.Err,
            TokenType.RedirectErrAppend => RedirectType.ErrAppend,
            TokenType.RedirectErrToOut => RedirectType.ErrToOut,
            TokenType.HereDoc => RedirectType.HereDoc,
            TokenType.HereString => RedirectType.HereString,
            _ => RedirectType.Out
        };

        cmd.Redirections.Add(new Redirection(type, target));
    }

    private static bool IsRedirect(TokenType type) =>
        type == TokenType.RedirectOut ||
        type == TokenType.RedirectAppend ||
        type == TokenType.RedirectIn ||
        type == TokenType.RedirectErr ||
        type == TokenType.RedirectErrAppend ||
        type == TokenType.RedirectErrToOut ||
        type == TokenType.HereDoc ||
        type == TokenType.HereString;

    private void SkipNewlines()
    {
        while (_pos < _tokens.Count && _tokens[_pos].Type == TokenType.Newline)
            _pos++;
    }

    private void ExpectKeyword(string keyword, int ownerLine, int ownerColumn)
    {
        if (Current.Type == TokenType.Word && Current.Value == keyword && !Current.WasQuoted)
        {
            Advance();
            return;
        }

        ThrowMissing(keyword, ownerLine, ownerColumn);
    }

    private void ExpectToken(TokenType type, string display, int ownerLine, int ownerColumn)
    {
        if (Current.Type == type)
        {
            Advance();
            return;
        }

        ThrowMissing(display, ownerLine, ownerColumn);
    }

    private void ThrowMissing(string expected, int ownerLine, int ownerColumn)
    {
        string found = Current.Type == TokenType.EOF ? "end of input" : $"`{Current.Value}`";
        throw new InvalidOperationException($"expected `{expected}` before {found} while parsing construct started at line {ownerLine}, column {ownerColumn}");
    }

    private Token Current => _pos < _tokens.Count ? _tokens[_pos] : new Token(TokenType.EOF, "", 0, 0);
    private Token Next => _pos + 1 < _tokens.Count ? _tokens[_pos + 1] : new Token(TokenType.EOF, "", 0, 0);
    private Token LookAhead(int k) => _pos + k < _tokens.Count ? _tokens[_pos + k] : new Token(TokenType.EOF, "", 0, 0);

    private void Advance()
    {
        if (_pos < _tokens.Count)
            _pos++;
    }
}
