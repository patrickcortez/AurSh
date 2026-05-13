using System.Text;

namespace AurShell.BlackBoxView;

/// <summary>
/// A line-buffered TextWriter that appends rendered text into a
/// BlackBoxSession's buffer. Used to retarget Console.Out / Console.Error
/// to the active BlackBox while a builtin runs.
/// </summary>
public sealed class BlackBoxWriter : System.IO.TextWriter
{
    private readonly BlackBoxSession _session;
    private readonly LineKind _kind;
    private readonly System.Action? _onLineAppended;
    private readonly StringBuilder _line = new();
    private readonly object _lock = new();

    public BlackBoxWriter(BlackBoxSession session, LineKind kind, System.Action? onLineAppended = null)
    {
        _session = session;
        _kind = kind;
        _onLineAppended = onLineAppended;
    }

    public override Encoding Encoding => Encoding.UTF8;

    public override void Write(char value)
    {
        lock (_lock)
        {
            if (value == '\n')
            {
                FlushLine();
            }
            else if (value == '\r')
            {
                // bare \r is treated as line reset; drop it for now
            }
            else
            {
                _line.Append(value);
            }
        }
    }

    public override void Write(string? value)
    {
        if (string.IsNullOrEmpty(value)) return;
        lock (_lock)
        {
            for (int i = 0; i < value!.Length; i++)
            {
                char c = value[i];
                if (c == '\n') FlushLine();
                else if (c == '\r') continue;
                else _line.Append(c);
            }
        }
    }

    public override void WriteLine(string? value)
    {
        Write(value);
        WriteLine();
    }

    public override void WriteLine()
    {
        lock (_lock)
        {
            FlushLine();
        }
    }

    public override void Flush()
    {
        lock (_lock)
        {
            if (_line.Length > 0)
                FlushLine();
        }
    }

    public void FlushPartial()
    {
        lock (_lock)
        {
            if (_line.Length == 0) return;
            FlushLine();
        }
    }

    private void FlushLine()
    {
        string text = _line.ToString();
        _line.Clear();
        _session.Buffer.Append(text, _kind);
        _onLineAppended?.Invoke();
    }
}
