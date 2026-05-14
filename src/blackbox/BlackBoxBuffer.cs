namespace AurShell.BlackBoxView;

public enum LineKind
{
    Stdout,
    Stderr,
    StdinEcho,
    Meta
}

public sealed class BufferLine
{
    public string Text { get; init; } = "";
    public LineKind Kind { get; init; } = LineKind.Stdout;
    public int? StageIndex { get; init; }

    public BufferLine() { }

    public BufferLine(string text, LineKind kind = LineKind.Stdout, int? stage = null)
    {
        Text = text;
        Kind = kind;
        StageIndex = stage;
    }
}

public sealed class BlackBoxBuffer
{
    private readonly List<BufferLine> _lines = new();
    private readonly int _capacity;
    private int _topLineIdx;
    private BufferLine? _partialLine;

    public BlackBoxBuffer(int capacity = 5000)
    {
        _capacity = System.Math.Max(64, capacity);
    }

    public int Count => _lines.Count;

    public BufferLine? PartialLine
    {
        get => _partialLine;
        set => _partialLine = value;
    }

    public int TopLineIdx => _topLineIdx;

    public BufferLine this[int index] => _lines[index];

    public void Append(BufferLine line)
    {
        _lines.Add(line);
        if (_lines.Count > _capacity)
        {
            int overflow = _lines.Count - _capacity;
            _lines.RemoveRange(0, overflow);
            _topLineIdx = System.Math.Max(0, _topLineIdx - overflow);
        }
    }

    public void Append(string text, LineKind kind = LineKind.Stdout, int? stage = null)
        => Append(new BufferLine(text, kind, stage));

    public void AppendMany(IEnumerable<string> lines, LineKind kind = LineKind.Stdout, int? stage = null)
    {
        foreach (string line in lines)
            Append(new BufferLine(line, kind, stage));
    }

    public IReadOnlyList<BufferLine> Snapshot() => _lines;

    public IEnumerable<BufferLine> Window(int top, int count)
    {
        int start = System.Math.Max(0, top);
        int end = System.Math.Min(_lines.Count, start + System.Math.Max(0, count));
        for (int i = start; i < end; i++)
            yield return _lines[i];
    }

    public void ScrollTo(int top)
    {
        if (_lines.Count == 0)
        {
            _topLineIdx = 0;
            return;
        }
        _topLineIdx = System.Math.Max(0, System.Math.Min(top, _lines.Count - 1));
    }

    public void ScrollToBottom(int visibleRows)
    {
        if (visibleRows <= 0 || _lines.Count <= visibleRows)
        {
            _topLineIdx = 0;
            return;
        }
        _topLineIdx = _lines.Count - visibleRows;
    }

    public void Clear()
    {
        _lines.Clear();
        _topLineIdx = 0;
    }
}
