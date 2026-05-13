namespace AurShell.BlackBoxView;

public sealed class BlackBox
{
    private int _nextId;
    private BlackBoxSession? _active;
    private readonly object _lock = new();

    public BlackBoxConfig Config { get; }
    public BlackBoxRenderer Renderer { get; }

    public BlackBox() : this(BlackBoxConfig.FromEnvironment()) { }

    public BlackBox(BlackBoxConfig config)
    {
        Config = config;
        Renderer = new BlackBoxRenderer(config);
    }

    public bool IsActive
    {
        get { lock (_lock) return _active != null; }
    }

    public BlackBoxSession? ActiveSession
    {
        get { lock (_lock) return _active; }
    }

    public BlackBoxSession Open(string commandLine, string? commandTitle = null, string? workingDirectory = null)
    {
        lock (_lock)
        {
            if (_active != null)
                throw new System.InvalidOperationException(
                    "BlackBox.Open called while another session is active. Close it first.");

            int id = ++_nextId;
            var buffer = new BlackBoxBuffer(Config.BufferLines);
            var session = new BlackBoxSession(
                id,
                commandLine,
                commandTitle ?? DeriveTitle(commandLine),
                workingDirectory ?? "",
                buffer,
                OnSessionDisposed);

            _active = session;
            return session;
        }
    }

    public void Repaint(BlackBoxSession session, System.IO.TextWriter? writer = null)
    {
        Renderer.Render(session, writer ?? System.Console.Out);
    }

    private void OnSessionDisposed(BlackBoxSession session)
    {
        lock (_lock)
        {
            if (ReferenceEquals(_active, session))
                _active = null;
        }
    }

    private static string DeriveTitle(string commandLine)
    {
        if (string.IsNullOrWhiteSpace(commandLine)) return "";

        string trimmed = commandLine.Trim();
        int firstBreak = trimmed.IndexOfAny(new[] { ' ', '\t' });
        string head = firstBreak < 0 ? trimmed : trimmed.Substring(0, firstBreak);

        if (trimmed.Contains('|'))
        {
            string[] parts = trimmed.Split('|', System.StringSplitOptions.RemoveEmptyEntries);
            var heads = new List<string>(parts.Length);
            foreach (string part in parts)
            {
                string p = part.Trim();
                int b = p.IndexOfAny(new[] { ' ', '\t' });
                heads.Add(b < 0 ? p : p.Substring(0, b));
            }
            return string.Join(" | ", heads);
        }

        return head;
    }
}
