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

    public int Execute(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return 0;

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

        return ExecuteList(ast);
    }

    public int ExecuteList(ListNode list)
    {
        int lastExit = 0;

        for (int i = 0; i < list.Entries.Count; i++)
        {
            var entry = list.Entries[i];

            if (i > 0)
            {
                var prevOp = list.Entries[i - 1].Operator;

                if (prevOp == ListOperator.And && lastExit != 0)
                    continue;

                if (prevOp == ListOperator.Or && lastExit == 0)
                    continue;
            }

            lastExit = ExecutePipeline(entry.Pipeline);
            _env.LastExitCode = lastExit;
        }

        return lastExit;
    }

    public int ExecutePipeline(PipelineNode pipeline)
    {
        if (pipeline.Commands.Count == 0)
            return 0;

        try
        {
            return Pipeline.Execute(pipeline, _env, _workingDirectory);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"aursh: execution error: {ex.Message}");
            return 1;
        }
        finally
        {
            SyncWorkingDirectory();
        }
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
}
