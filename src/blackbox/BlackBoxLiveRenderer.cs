using System.Text;
using AurShell.Utils;

namespace AurShell.BlackBoxView;

/// <summary>
/// Streaming renderer that paints the box in place by rewinding over the
/// previous render with cursor-up + erase before re-emitting.
/// </summary>
public sealed class BlackBoxLiveRenderer
{
    private readonly BlackBoxRenderer _renderer;
    private readonly object _lock = new();
    private int _previousHeight;
    private bool _started;
    private bool _completed;
    private bool _cursorHidden;
    private DateTime _lastRender = DateTime.MinValue;
    private readonly TimeSpan _minInterval = TimeSpan.FromMilliseconds(33);

    public BlackBoxLiveRenderer(BlackBoxRenderer renderer)
    {
        _renderer = renderer;
    }

    /// <summary>
    /// Open the box in passthrough mode: only the top header is painted; the
    /// child process is expected to write directly to the terminal between the
    /// header and the eventual footer (FinishPassthrough). Used for interactive
    /// REPLs on platforms where we cannot allocate a pseudo-terminal (Windows).
    /// </summary>
    public void StartPassthrough(BlackBoxSession session, System.IO.TextWriter writer)
    {
        lock (_lock)
        {
            _started = true;
            _completed = false;
            _previousHeight = 0;
            _lastRender = DateTime.MinValue;

            // Don't hide the cursor in passthrough mode — the child needs it
            // visible so the user can see what they're typing.
            _renderer.RenderHeaderOnly(session, writer);
        }
    }

    /// <summary>
    /// Close a passthrough box: emit the footer below wherever the child left
    /// the cursor. We forcibly bring the cursor to column 0 on a new line first.
    /// </summary>
    public void FinishPassthrough(BlackBoxSession session, System.IO.TextWriter writer)
    {
        lock (_lock)
        {
            if (!_started || _completed) return;
            _completed = true;

            // Ensure footer starts on a fresh line even if the child's last
            // output ended mid-line (no trailing \n).
            try
            {
                writer.Write("\r\n");
                writer.Flush();
            }
            catch { }

            _renderer.RenderFooterOnly(session, writer);
        }
    }

    public void Start(BlackBoxSession session, System.IO.TextWriter writer)
    {
        lock (_lock)
        {
            // Reset per-session state so the renderer can be reused for the next
            // command. Previously _started/_completed stuck at true after the
            // first command, which made every subsequent Start/Update/Finish a
            // no-op (boxes never re-painted).
            _started = true;
            _completed = false;
            _previousHeight = 0;
            _lastRender = DateTime.MinValue;

            HideCursor(writer);
            RenderInternal(session, writer, force: true);
        }
    }

    public void Update(BlackBoxSession session, System.IO.TextWriter writer)
    {
        lock (_lock)
        {
            if (!_started || _completed) return;

            if ((DateTime.UtcNow - _lastRender) < _minInterval)
                return;

            RenderInternal(session, writer, force: false);
        }
    }

    public void ForceUpdate(BlackBoxSession session, System.IO.TextWriter writer)
    {
        lock (_lock)
        {
            if (!_started || _completed) return;
            RenderInternal(session, writer, force: true);
        }
    }

    public void Finish(BlackBoxSession session, System.IO.TextWriter writer)
    {
        lock (_lock)
        {
            if (!_started || _completed) return;
            _completed = true;

            RenderInternal(session, writer, force: true);
            ShowCursor(writer);
        }
    }

    public void Abort(BlackBoxSession session, System.IO.TextWriter writer)
    {
        lock (_lock)
        {
            if (!_started) { ShowCursor(writer); return; }
            if (_completed) return;
            _completed = true;

            session.MarkAborted();
            RenderInternal(session, writer, force: true);
            ShowCursor(writer);
        }
    }

    private void RenderInternal(BlackBoxSession session, System.IO.TextWriter writer, bool force)
    {
        var sb = new StringBuilder();
        if (_previousHeight > 0)
        {
            sb.Append(Ansi.MoveCursorUp(_previousHeight));
            sb.Append('\r');
            sb.Append(Ansi.ClearScreenFromCursor);
        }

        string painted = CaptureRender(session);
        sb.Append(painted);

        writer.Write(sb.ToString());
        writer.Flush();

        _previousHeight = CountNewlines(painted);
        _lastRender = DateTime.UtcNow;
        _ = force;
    }

    private string CaptureRender(BlackBoxSession session)
    {
        var sw = new System.IO.StringWriter();
        _renderer.Render(session, sw);
        return sw.ToString();
    }

    private static int CountNewlines(string s)
    {
        int n = 0;
        for (int i = 0; i < s.Length; i++)
            if (s[i] == '\n') n++;
        return n;
    }

    private void HideCursor(System.IO.TextWriter writer)
    {
        if (_cursorHidden) return;
        try
        {
            writer.Write(Ansi.CursorHide);
            writer.Flush();
            _cursorHidden = true;
        }
        catch { }
    }

    private void ShowCursor(System.IO.TextWriter writer)
    {
        if (!_cursorHidden) return;
        try
        {
            writer.Write(Ansi.CursorShow);
            writer.Flush();
            _cursorHidden = false;
        }
        catch { }
    }
}
