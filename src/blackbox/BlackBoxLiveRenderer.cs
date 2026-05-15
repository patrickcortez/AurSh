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
    private int _lastTransientRows;
    private int _lastCursorRowOffset;
    private bool _started;
    private bool _completed;
    private bool _cursorHidden;
    private DateTime _lastUpdate = DateTime.MinValue;
    private readonly TimeSpan _minInterval = TimeSpan.FromMilliseconds(33);

    // Active session + writer for the resize callback. Only valid between
    // Start() and Finish()/Abort(). The resize handler is fired on a
    // background thread (TerminalSize timer/SIGWINCH), so all access is
    // guarded by _lock.
    private BlackBoxSession? _activeSession;
    private System.IO.TextWriter? _activeWriter;
    private int _lastRenderedWidth;
    private LayoutTier _lastRenderedTier;
    private bool _passthrough;
    private bool _altScreen;
    private Action<int, int>? _resizeHandler;

    /// <summary>
    /// True between an <see cref="EnterAltScreen"/> call and the next
    /// <see cref="Finish"/>/<see cref="Abort"/>/reset. The output pumps in
    /// <c>BlackBoxIo</c> check this to decide whether to forward the child's
    /// bytes raw to the real terminal (alt-screen takeover) or to keep
    /// streaming them into the box buffer as normal lines.
    /// </summary>
    public bool IsAltScreenActive
    {
        get { lock (_lock) return _altScreen; }
    }

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
            _passthrough = true;
            _activeSession = session;
            _activeWriter = writer;
            _lastRenderedWidth = TerminalSize.Width;
            _lastRenderedTier = _renderer.Tier;
            // Don't subscribe to resize in passthrough mode: the child
            // process owns the screen between header and footer, so we'd
            // have nothing to redraw without corrupting its output.
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
            DetachResize();
            _activeSession = null;
            _activeWriter = null;
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
            _activeSession = session;
            _activeWriter = writer;
            _lastRenderedWidth = TerminalSize.Width;
            _lastRenderedTier = _renderer.Tier;
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
            _lastTransientRows = 1;
            _lastCursorRowOffset = 0;
            _lastUpdate = DateTime.UtcNow;

            AttachResize();
        }
    }

    public void Update(BlackBoxSession session, System.IO.TextWriter writer)
    {
        lock (_lock)
        {
            if (!_started || _completed) return;
            // Once the child has taken over the alt screen, the box header
            // is already drawn and a child app is owning the display. We
            // must NOT keep painting body rows over its UI.
            if (_altScreen) return;
            if ((DateTime.UtcNow - _lastUpdate) < _minInterval) return;
            EmitPending(session, writer);
            _lastUpdate = DateTime.UtcNow;
        }
    }

    /// <summary>
    /// Called by the byte pump when it detects that the child process has
    /// entered the terminal's alternate screen buffer (DECSET 1049/1047/47).
    /// Drains any pending body rows into the box, then suspends further body
    /// emission so the child's TUI can take over the display unobstructed.
    /// The footer continues to be re-emitted by <see cref="Finish"/> once the
    /// child exits, so the user still sees "exit:N <elapsed>" afterwards.
    /// </summary>
    public void EnterAltScreen(BlackBoxSession session, System.IO.TextWriter writer)
    {
        lock (_lock)
        {
            if (!_started || _completed) return;
            if (_altScreen) return;

            // Flush whatever rows we had buffered up to the alt-screen entry.
            try { EmitPending(session, writer); } catch { }

            _altScreen = true;

            ShowCursor(writer);
            try { writer.Flush(); } catch { }
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

            if (_altScreen)
            {

                try
                {
                    writer.Write("\x1b[?1049l");
                    writer.Flush();
                }
                catch { }
                _altScreen = false;
            }

            // Drain any remaining body rows and emit the final footer (with
            // exit code / elapsed time).
            EmitPending(session, writer);
            ShowCursor(writer);
            DetachResize();
            _activeSession = null;
            _activeWriter = null;
        }
    }

    public void Abort(BlackBoxSession session, System.IO.TextWriter writer)
    {
        lock (_lock)
        {
            if (!_started) { ShowCursor(writer); return; }
            if (_completed) return;
            _completed = true;

            if (_altScreen)
            {
                try
                {
                    writer.Write("\x1b[?1049l");
                    writer.Flush();
                }
                catch { }
                _altScreen = false;
            }

            session.MarkAborted();
            EmitPending(session, writer);
            ShowCursor(writer);
            DetachResize();
            _activeSession = null;
            _activeWriter = null;
        }
    }

    /// <summary>
    /// Move cursor up to overwrite the previously emitted footer and active input, append any
    /// new body rows that have arrived since the last render, then re-emit the active input
    /// and footer (which now reflects current elapsed time / final exit code).
    /// </summary>
    private void EmitPending(BlackBoxSession session, System.IO.TextWriter writer)
    {
        int bufCount = session.Buffer.Count;
        var (inputLine, inputCursor) = session.GetInput();
        var partialLine = session.Buffer.PartialLine;
        bool hasInput = !string.IsNullOrEmpty(inputLine);
        bool hasPartial = partialLine != null;

        bool needFooterRefresh = !_footerEmitted || bufCount > _emittedBodyRows || _completed || hasInput || hasPartial;
        if (!needFooterRefresh) return;

        var sb = new StringBuilder();

        if (_footerEmitted)
        {
            // If we moved the cursor up to show the input cursor last time, 
            // move it back down to the baseline (the line below the footer) 
            // before calculating the move-up to clear the transient area.
            if (_lastCursorRowOffset > 0)
            {
                sb.Append(Ansi.MoveCursorDown(_lastCursorRowOffset));
                sb.Append('\r');
            }

            // Move up by the number of transient rows emitted last time (footer + any input rows).
            sb.Append(Ansi.MoveCursorUp(_lastTransientRows > 0 ? _lastTransientRows : 1));
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

        int transientRows = 0;
        int cursorRowOffset = 0;
        int cursorColOffset = 0;

        if ((hasInput || hasPartial) && !_completed)
        {
            int innerWidth = System.Math.Max(4, _lastRenderedWidth - (_renderer.Tier == LayoutTier.Bar ? 0 : 2));
            int contentWidth = System.Math.Max(1, innerWidth - 2);

            string partialText = partialLine?.Text ?? "";
            string fullText = partialText + inputLine;
            
            // Calculate physical cursor position based on visible length of the partial line
            int cursorPhysicalIndex = Ansi.VisibleLength(partialText) + inputCursor;

            var chunks = SplitIntoChunks(fullText, contentWidth);
            if (chunks.Count == 0) chunks.Add("");

            for (int i = 0; i < chunks.Count; i++)
            {
                var kind = partialLine?.Kind ?? LineKind.Stdout;
                string renderedRow = _renderer.RenderBodyRowToString(new BufferLine(chunks[i], kind, partialLine?.StageIndex));
                sb.Append(renderedRow);
                sb.Append('\n');
                transientRows++;
            }

            int cursorChunk = cursorPhysicalIndex / contentWidth;
            int cursorCol = cursorPhysicalIndex % contentWidth;
            
            cursorRowOffset = 1 + (chunks.Count - cursorChunk);
            cursorColOffset = (_renderer.Tier == LayoutTier.Compact || _renderer.Tier == LayoutTier.Bar) ? cursorCol : cursorCol + 2;
        }

        // Re-emit the footer in its current state.
        sb.Append(_renderer.RenderFooterToString(session));
        sb.Append('\n');
        transientRows++;

        _lastTransientRows = transientRows;
        _lastCursorRowOffset = ((hasInput || hasPartial) && !_completed) ? cursorRowOffset : 0;
        _footerEmitted = true;

        if ((hasInput || hasPartial) && !_completed && cursorRowOffset > 0)
        {
            sb.Append(Ansi.MoveCursorUp(cursorRowOffset));
            sb.Append('\r');
            if (cursorColOffset > 0)
                sb.Append(Ansi.MoveCursorRight(cursorColOffset));
        }

        writer.Write(sb.ToString());
        writer.Flush();

        if ((hasInput || hasPartial) && !_completed)
        {
            ShowCursor(writer);
        }
        else
        {
            HideCursor(writer);
        }
    }

    private List<string> SplitIntoChunks(string text, int chunkLength)
    {
        var list = new List<string>();
        if (string.IsNullOrEmpty(text)) return list;
        for (int i = 0; i < text.Length; i += chunkLength)
        {
            int len = System.Math.Min(chunkLength, text.Length - i);
            list.Add(text.Substring(i, len));
        }
        return list;
    }

    private void ResetState()
    {
        _completed = false;
        _started = false;
        _emittedBodyRows = 0;
        _footerEmitted = false;
        _lastTransientRows = 0;
        _lastCursorRowOffset = 0;
        _lastUpdate = DateTime.MinValue;
        _passthrough = false;
        _altScreen = false;
        _lastRenderedWidth = 0;
        _lastRenderedTier = LayoutTier.Full;
        DetachResize();
    }

    private void AttachResize()
    {
        if (_resizeHandler != null) return;
        _resizeHandler = (_, _) => OnTerminalResized();
        TerminalSize.Changed += _resizeHandler;
    }

    private void DetachResize()
    {
        if (_resizeHandler == null) return;
        TerminalSize.Changed -= _resizeHandler;
        _resizeHandler = null;
    }

    /// <summary>
    /// Called on a background thread when the terminal is resized. Triggers
    /// a redraw of the *body and footer* of the current box at the new
    /// width. The header and any rows already past the viewport stay at
    /// their old width — we cannot retroactively edit terminal scrollback,
    /// so trying to reflow them would produce visible corruption.
    /// </summary>
    private void OnTerminalResized()
    {
        lock (_lock)
        {
            if (!_started || _completed) return;
            if (_passthrough) return; // child owns the screen
            BlackBoxSession? session = _activeSession;
            System.IO.TextWriter? writer = _activeWriter;
            if (session == null || writer == null) return;

            int newWidth = TerminalSize.Width;
            LayoutTier newTier = _renderer.Tier;
            if (newWidth == _lastRenderedWidth && newTier == _lastRenderedTier) return;


            try
            {
                _emittedBodyRows = 0;
                EmitPending(session, writer);
                _lastRenderedWidth = newWidth;
                _lastRenderedTier = newTier;
            }
            catch(Exception ex)
            {
                Console.Error.WriteLine($"{Ansi.FgRed}AurSh: {ex.Message} | {ex.StackTrace}");
            }
        }
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
