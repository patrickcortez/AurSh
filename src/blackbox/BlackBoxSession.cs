namespace AurShell.BlackBoxView;

public enum BlackBoxState
{
    Pending,
    Running,
    Finished,
    Aborted
}

public sealed class BlackBoxSession : System.IDisposable
{
    private readonly System.Action<BlackBoxSession> _onDispose;
    private bool _disposed;

    public int Id { get; }
    public string CommandLine { get; }
    public string CommandTitle { get; }
    public string WorkingDirectory { get; }
    public System.DateTime StartedAt { get; }
    public System.DateTime? FinishedAt { get; private set; }
    public int? ExitCode { get; private set; }
    public BlackBoxState State { get; private set; }
    public bool TtyBypassed { get; private set; }

    public BlackBoxBuffer Buffer { get; }

    /// <summary>The real terminal stdout, captured at Open() before any redirection.</summary>
    public System.IO.TextWriter TerminalOut { get; }

    public BlackBoxSession(
        int id,
        string commandLine,
        string commandTitle,
        string workingDirectory,
        BlackBoxBuffer buffer,
        System.Action<BlackBoxSession> onDispose)
    {
        Id = id;
        CommandLine = commandLine ?? "";
        CommandTitle = string.IsNullOrEmpty(commandTitle) ? CommandLine : commandTitle;
        WorkingDirectory = workingDirectory ?? "";
        Buffer = buffer;
        StartedAt = System.DateTime.UtcNow;
        State = BlackBoxState.Running;
        TerminalOut = System.Console.Out;
        _onDispose = onDispose;
    }

    public System.TimeSpan Elapsed => (FinishedAt ?? System.DateTime.UtcNow) - StartedAt;

    public void SetExitCode(int code)
    {
        ExitCode = code;
        FinishedAt = System.DateTime.UtcNow;
        State = BlackBoxState.Finished;
    }

    public void MarkAborted()
    {
        FinishedAt = System.DateTime.UtcNow;
        State = BlackBoxState.Aborted;
    }

    public void MarkTtyBypassed()
    {
        TtyBypassed = true;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (State == BlackBoxState.Running)
            MarkAborted();

        _onDispose(this);
    }
}
