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

public class CommandNode
{
    public string Name { get; set; } = "";
    public string RawExpandedName { get; set; } = "";
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

public enum ListOperator
{
    Sequential,
    And,
    Or
}

public class PipelineNode
{
    public List<CommandNode> Commands { get; } = new();
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

    public ListNode Parse()
    {
        var list = new ListNode();

        SkipNewlines();

        while (Current.Type != TokenType.EOF)
        {
            var pipeline = ParsePipeline();
            if (pipeline == null || pipeline.Commands.Count == 0)
            {
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
        var pipeline = new PipelineNode();

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

    private CommandNode? ParseCommand()
    {
        var cmd = new CommandNode();
        bool hasContent = false;

        while (Current.Type == TokenType.Word)
        {
            if (!hasContent)
            {
                cmd.Name = Current.Value;
                cmd.RawExpandedName = Current.RawExpandedValue;
                hasContent = true;
            }
            else
            {
                cmd.Args.Add(Current.Value);
                cmd.RawExpandedArgs.Add(Current.RawExpandedValue);
            }
            Advance();

            while (IsRedirect(Current.Type))
            {
                ParseRedirection(cmd);
            }
        }

        while (IsRedirect(Current.Type))
        {
            ParseRedirection(cmd);
            hasContent = true;
        }

        return hasContent ? cmd : null;
    }

    private void ParseRedirection(CommandNode cmd)
    {
        TokenType redirectType = Current.Type;
        Advance();

        if (Current.Type != TokenType.Word)
        {
            Console.Error.WriteLine("aursh: syntax error near unexpected token");
            return;
        }

        string target = Current.Value;
        Advance();

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

    private Token Current => _pos < _tokens.Count ? _tokens[_pos] : new Token(TokenType.EOF, "");

    private void Advance()
    {
        if (_pos < _tokens.Count)
            _pos++;
    }
}
