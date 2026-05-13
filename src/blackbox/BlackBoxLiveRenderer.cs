using System.Text;
using AurShell.Utils;

namespace AurShell.BlackBoxView;

/// <summary>
/// Streaming-append live renderer for BlackBox sessions.
///
/// The full-redraw strategy (cursor-up the entire previous frame, clear, repaint)
/// breaks down the moment the box height exceeds the terminal viewport: the
/// upper portion of the box scrolls into the terminal's scrollback where ANSI
/// cursor-up can no longer reach it, so subsequent re-renders only clear/redraw
/// the visible region and leave duplicated tops stuck in history.
///
/// Instead, we emit the header ONCE, append body rows progressively below it
/// (old rows naturally and *correctly* scroll into history), and only ever
/// rewind the single footer line for updates. That keeps the per-update
/// cursor-up at exactly 1 row regardless of how tall the box has grown, which
/// always works because the footer is always inside the visible viewport.
/// </summary>
public sealed class BlackBoxLiveRenderer
{
    private readonly BlackBoxRenderer _renderer;
    private readonly object _lock = new();
    private int _emittedBodyRows;
    private bool _footerEmitted;
    private bool _started;
    private bool _completed;
    private bool _cursorHidden;
    private DateTime _lastUpdate = DateTime.MinValue;
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
            ResetState();
            _started = true;
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

            try
            {
                writer.Write("\r\n");
                writer.Flush();
            }
            catch { }

            _renderer.RenderFooterOnly(session, writer);
        }
    }

    /// <summary>
    /// Open a normal (boxed-body) session: emit the header, then the initial
    /// footer right below it. Body rows will be appended between them on Update.
    /// </summary>
    public void Start(BlackBoxSession session, System.IO.TextWriter writer)
    {
        lock (_lock)
        {
            ResetState();
            _started = true;
            HideCursor(writer);

            var sb = new StringBuilder();
            sb.Append(_renderer.RenderFooterToString(session));
            // The header is emitted via RenderHeaderOnly which already writes a
            // trailing newline; the footer string above has no trailing newline
            // and is appended just below the header.
            _renderer.RenderHeaderOnly(session, writer);
            writer.Write(sb.ToString());
            writer.Write('\n');
            writer.Flush();
            _footerEmitted = true;
            _lastUpdate = DateTime.UtcNow;
        }
    }

    public void Update(BlackBoxSession session, System.IO.TextWriter writer)
    {
        lock (_lock)
        {
            if (!_started || _completed) return;
            if ((DateTime.UtcNow - _lastUpdate) < _minInterval) return;
            EmitPending(session, writer);
            _lastUpdate = DateTime.UtcNow;
        }
    }

    public void ForceUpdate(BlackBoxSession session, System.IO.TextWriter writer)
    {
        lock (_lock)
        {
            if (!_started || _completed) return;
            EmitPending(session, writer);
            _lastUpdate = DateTime.UtcNow;
        }
    }

    public void Finish(BlackBoxSession session, System.IO.TextWriter writer)
    {
        lock (_lock)
        {
            if (!_started || _completed) return;
            _completed = true;

            // Drain any remaining body rows and emit the final footer (with
            // exit code / elapsed time).
            EmitPending(session, writer);
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
            EmitPending(session, writer);
            ShowCursor(writer);
        }
    }

    /// <summary>
    /// Move cursor up to overwrite the previously emitted footer, append any
    /// new body rows that have arrived since the last render, then re-emit the
    /// footer (which now reflects current elapsed time / final exit code).
    /// </summary>
    private void EmitPending(BlackBoxSession session, System.IO.TextWriter writer)
    {
        int bufCount = session.Buffer.Count;
        bool needFooterRefresh = !_footerEmitted || bufCount > _emittedBodyRows || _completed;
        if (!needFooterRefresh) return;

        var sb = new StringBuilder();

        if (_footerEmitted)
        {
            // Cursor is on the blank line below the previous footer. Move up 1
            // to land on the footer line itself, then \r to col 0, then clear
            // from cursor to end of screen. Since the footer is always 1 row,
            // this MoveCursorUp(1) is always within the viewport.
            sb.Append(Ansi.MoveCursorUp(1));
            sb.Append('\r');
            sb.Append(Ansi.ClearScreenFromCursor);
        }

        // Append every body row we haven't emitted yet.
        for (int i = _emittedBodyRows; i < bufCount; i++)
        {
            BufferLine line = session.Buffer[i];
            sb.Append(_renderer.RenderBodyRowToString(line));
            sb.Append('\n');
        }
        _emittedBodyRows = bufCount;

        // Re-emit the footer in its current state.
        sb.Append(_renderer.RenderFooterToString(session));
        sb.Append('\n');
        _footerEmitted = true;

        writer.Write(sb.ToString());
        writer.Flush();
    }

    private void ResetState()
    {
        _completed = false;
        _started = false;
        _emittedBodyRows = 0;
        _footerEmitted = false;
        _lastUpdate = DateTime.MinValue;
    }

    private void HideCursor(System.IO.TextWriter writer)
    {
        if (_cursorHidden) return;
        try { writer.Write(Ansi.CursorHide); writer.Flush(); _cursorHidden = true; } catch { }
    }

    private void ShowCursor(System.IO.TextWriter writer)
    {
        if (!_cursorHidden) return;
        try { writer.Write(Ansi.CursorShow); writer.Flush(); _cursorHidden = false; } catch { }
    }
}
