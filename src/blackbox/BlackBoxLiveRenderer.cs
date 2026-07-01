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
    private int _lastEmittedPhysicalRows;
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
            _activeSession = session;
            _activeWriter = writer;
            _lastRenderedWidth = TerminalSize.Width;
            _lastRenderedTier = BlackBoxRenderer.ResolveTier(_lastRenderedWidth);
            // Don't subscribe to resize in passthrough mode: the child
            // process owns the screen between header and footer, so we'd
            // have nothing to redraw without corrupting its output.
            _renderer.RenderHeaderOnly(session, writer, _lastRenderedWidth, _lastRenderedTier);
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
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"aursh error: {ex.Message}"); }

            _renderer.RenderFooterOnly(session, writer, _lastRenderedWidth, _lastRenderedTier);
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
            _lastRenderedTier = BlackBoxRenderer.ResolveTier(_lastRenderedWidth);
            HideCursor(writer);

            var sb = new StringBuilder();
            sb.Append(_renderer.RenderFooterToString(session, _lastRenderedWidth, _lastRenderedTier));
            // The header is emitted via RenderHeaderOnly which already writes a
            // trailing newline; the footer string above has no trailing newline
            // and is appended just below the header.
            _renderer.RenderHeaderOnly(session, writer, _lastRenderedWidth, _lastRenderedTier);
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
            try { EmitPending(session, writer); } catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"aursh error: {ex.Message}"); }

            _altScreen = true;

            ShowCursor(writer);
            try { writer.Flush(); } catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"aursh error: {ex.Message}"); }
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
                catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"aursh error: {ex.Message}"); }
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
                catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"aursh error: {ex.Message}"); }
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

        // Check if the last body row was overwritten via ReplaceLast (carriage-
        // return-based in-place updates from F# interactive, progress bars, etc.)
        bool dirtyLast = session.Buffer.LastLineDirty;

        bool needFooterRefresh = !_footerEmitted || bufCount > _emittedBodyRows || _completed || hasInput || hasPartial || dirtyLast;
        if (!needFooterRefresh) return;

        // If the last emitted body row was replaced in the buffer, we need to
        // back up one extra row so the for-loop below re-emits it. Decrement
        // the emitted counter so it gets picked up, and add 1 to the cursor-up
        // count so we overwrite the stale row on screen.
        int extraUp = 0;
        if (dirtyLast && _emittedBodyRows > 0 && _emittedBodyRows >= bufCount)
        {
            _emittedBodyRows--;
            extraUp = _lastEmittedPhysicalRows > 0 ? _lastEmittedPhysicalRows : 1;
            session.Buffer.LastLineDirty = false;
        }
        else if (dirtyLast)
        {
            session.Buffer.LastLineDirty = false;
        }

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

            // Move up by the number of transient rows emitted last time
            // (footer + any input rows), plus one more if we're re-drawing
            // a dirty body row that was overwritten by ReplaceLast.
            int upCount = (_lastTransientRows > 0 ? _lastTransientRows : 1) + extraUp;
            sb.Append(Ansi.MoveCursorUp(upCount));
            sb.Append('\r');
            sb.Append(Ansi.ClearScreenFromCursor);
        }

        // Append every body row we haven't emitted yet (including any
        // row we backed up to re-draw due to a ReplaceLast overwrite).
        for (int i = _emittedBodyRows; i < bufCount; i++)
        {
            BufferLine line = session.Buffer[i];
            List<string> physicalRows = _renderer.RenderBodyRows(line, _lastRenderedWidth, _lastRenderedTier);
            foreach (string r in physicalRows)
            {
                sb.Append(r);
                sb.Append('\n');
            }
            if (i == bufCount - 1)
            {
                _lastEmittedPhysicalRows = physicalRows.Count;
            }
        }
        _emittedBodyRows = bufCount;

        int transientRows = 0;
        int cursorRowOffset = 0;
        int cursorColOffset = 0;

        if ((hasInput || hasPartial) && !_completed)
        {
            int innerWidth = System.Math.Max(4, _lastRenderedWidth - (_lastRenderedTier == LayoutTier.Bar ? 0 : 2));
            int contentWidth = System.Math.Max(1, innerWidth - 2);

            string partialText = partialLine?.Text ?? "";
            string fullText = partialText + inputLine;

            // Calculate physical cursor position based on visible length of the partial line
            int cursorPhysicalIndex = Ansi.VisibleLength(partialText) + inputCursor;

            var chunks = Ansi.SplitVisible(fullText, contentWidth);
            if (chunks.Count == 0) chunks.Add("");

            for (int i = 0; i < chunks.Count; i++)
            {
                var kind = partialLine?.Kind ?? LineKind.Stdout;
                var renderedRows = _renderer.RenderBodyRows(new BufferLine(chunks[i], kind, partialLine?.StageIndex), _lastRenderedWidth, _lastRenderedTier);
                foreach (string renderedRow in renderedRows)
                {
                    sb.Append(renderedRow);
                    sb.Append('\n');
                    transientRows++;
                }
            }

            int cursorChunk = cursorPhysicalIndex / contentWidth;
            int cursorCol = cursorPhysicalIndex % contentWidth;

            cursorRowOffset = 1 + (chunks.Count - cursorChunk);
            cursorColOffset = (_lastRenderedTier == LayoutTier.Compact || _lastRenderedTier == LayoutTier.Bar) ? cursorCol : cursorCol + 2;
        }

        // Re-emit the footer in its current state.
        sb.Append(_renderer.RenderFooterToString(session, _lastRenderedWidth, _lastRenderedTier));
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



    private void ResetState()
    {
        _completed = false;
        _started = false;
        _emittedBodyRows = 0;
        _lastEmittedPhysicalRows = 0;
        _footerEmitted = false;
        _lastTransientRows = 0;
        _lastCursorRowOffset = 0;
        _lastUpdate = DateTime.MinValue;
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
        // Dimensions are frozen for the lifetime of the active session.
        // The terminal's native text wrapping handles the visual adjustment.
        // New dimensions will be picked up by the next session's Start().
    }

    private void HideCursor(System.IO.TextWriter writer)
    {
        if (_cursorHidden) return;
        try { writer.Write(Ansi.CursorHide); writer.Flush(); _cursorHidden = true; } catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"aursh error: {ex.Message}"); }
    }

    private void ShowCursor(System.IO.TextWriter writer)
    {
        if (!_cursorHidden) return;
        try { writer.Write(Ansi.CursorShow); writer.Flush(); _cursorHidden = false; } catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"aursh error: {ex.Message}"); }
    }
}
