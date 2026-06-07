using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AurShell.Utils;

namespace AurShell.BlackBoxView;

/// <summary>
/// Bridges spawned-process stdio (and pipeline tees) into a BlackBoxSession's
/// buffer. Encapsulates line-aware byte pumping and stdin keystroke forwarding.
/// </summary>
public static class BlackBoxIo
{
    /// <summary>
    /// Configure a child's <see cref="ProcessStartInfo"/> to redirect its stdio
    /// so we can intercept it. Any streams the caller already redirected
    /// (e.g. for explicit user redirections like `cmd > file`) are left alone.
    /// </summary>
    public static void PrepareForBox(ProcessStartInfo psi, BoxRedirectFlags userFlags)
    {
        if (!userFlags.StdoutRedirected) psi.RedirectStandardOutput = true;
        if (!userFlags.StderrRedirected) psi.RedirectStandardError = true;
        if (!userFlags.StdinRedirected) psi.RedirectStandardInput = true;
        psi.StandardOutputEncoding = Encoding.UTF8;
        psi.StandardErrorEncoding = Encoding.UTF8;
    }

    /// <summary>
    /// Pump a child's stdout/stderr/stdin through the BlackBox session, and
    /// run a live re-render after each line is appended. Returns when both
    /// stdout and stderr are drained.
    /// </summary>
    public static async Task PumpAsync(
        Process process,
        BlackBoxSession session,
        BoxRedirectFlags userFlags,
        BlackBox owner,
        int? stageIndex,
        Stream? stdoutForward,
        CancellationToken cancellation)
    {
        var tasks = new System.Collections.Generic.List<Task>();

        if (process.StartInfo.RedirectStandardOutput && !userFlags.StdoutRedirected)
        {
            tasks.Add(PumpToBufferAsync(
                process.StandardOutput.BaseStream,
                session,
                LineKind.Stdout,
                stageIndex,
                stdoutForward,
                owner,
                cancellation));
        }

        if (process.StartInfo.RedirectStandardError && !userFlags.StderrRedirected)
        {
            tasks.Add(PumpToBufferAsync(
                process.StandardError.BaseStream,
                session,
                LineKind.Stderr,
                stageIndex,
                null,
                owner,
                cancellation));
        }

        CancellationTokenSource? stdinCancel = null;
        Task? stdinTask = null;
        if (process.StartInfo.RedirectStandardInput && !userFlags.StdinRedirected)
        {
            stdinCancel = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            stdinTask = ForwardStdinAsync(process, session, owner.LiveRenderer, stdinCancel.Token);
        }

        try
        {
            await Task.WhenAll(tasks).ConfigureAwait(false);
        }
        finally
        {
            stdinCancel?.Cancel();
            if (stdinTask != null)
            {
                try { await stdinTask.ConfigureAwait(false); } catch { }
            }
            stdinCancel?.Dispose();
        }
    }

    public static async Task PumpToBufferAsync(
        Stream source,
        BlackBoxSession session,
        LineKind kind,
        int? stageIndex,
        Stream? forwardTo,
        BlackBox owner,
        CancellationToken cancellation)
    {
        var lineBuf = new MemoryStream();
        byte[] buffer = new byte[8192];
        var sniffer = new AltScreenSniffer();
        var ansiTracker = new AnsiStateTracker();
        Stream? rawOut = null;
        bool pendingCr = false;  // tracks if last byte was \r (for CR-overwrite)

        try
        {
            while (!cancellation.IsCancellationRequested)
            {
                int read = await source.ReadAsync(buffer, 0, buffer.Length, cancellation).ConfigureAwait(false);
                if (read <= 0) break;

                if (forwardTo != null)
                {
                    try
                    {
                        await forwardTo.WriteAsync(buffer, 0, read, cancellation).ConfigureAwait(false);
                        await forwardTo.FlushAsync(cancellation).ConfigureAwait(false);
                    }
                    catch { }
                }

                if (owner.LiveRenderer.IsAltScreenActive)
                {
                    if (rawOut == null) rawOut = System.Console.OpenStandardOutput();
                    try
                    {
                        await rawOut.WriteAsync(buffer, 0, read, cancellation).ConfigureAwait(false);
                        await rawOut.FlushAsync(cancellation).ConfigureAwait(false);
                    }
                    catch { }
                    continue;
                }

                for (int i = 0; i < read; i++)
                {
                    byte b = buffer[i];

                    // Sniff for DECSET 1049/1047/47 (enter alternate screen).
                    int beforeLen = (int)lineBuf.Length;
                    int sniffEscStart = sniffer.EscStartIdx;
                    AltScreenResult r = sniffer.Feed(b, beforeLen);

                    if (b == (byte)'\n')
                    {
                        string rawLine = StripTrailingCr(Encoding.UTF8.GetString(lineBuf.GetBuffer(), 0, (int)lineBuf.Length));
                        rawLine = StripCursorSequences(rawLine);

                        if (kind == LineKind.Stderr && rawLine == "#< CLIXML")
                        {
                            lineBuf.SetLength(0);
                            continue;
                        }

                        if (kind == LineKind.Stderr && rawLine.StartsWith("<Objs") && rawLine.EndsWith("</Objs>"))
                        {
                            var matches = System.Text.RegularExpressions.Regex.Matches(rawLine, @"<S S=""Error"">([^<]*)</S>");
                            foreach (System.Text.RegularExpressions.Match m in matches)
                            {
                                string decoded = System.Text.RegularExpressions.Regex.Replace(m.Groups[1].Value, @"_x([0-9a-fA-F]{4})_", match =>
                                    ((char)Convert.ToInt32(match.Groups[1].Value, 16)).ToString());

                                string[] split = decoded.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
                                foreach (var s in split)
                                {
                                    if (string.IsNullOrEmpty(s)) continue;
                                    session.Buffer.Append(s, kind, stageIndex);
                                }
                            }
                            session.Buffer.PartialLine = null;
                            lineBuf.SetLength(0);
                            owner.LiveRenderer.Update(session, session.TerminalOut);
                            continue;
                        }

                        string statePrefix = ansiTracker.GetStatePrefix();
                        string line = statePrefix.Length > 0 ? statePrefix + rawLine : rawLine;
                        ansiTracker.ProcessLine(rawLine);

                        if (pendingCr)
                        {
                            session.Buffer.ReplaceLast(line, kind, stageIndex);
                            pendingCr = false;
                        }
                        else
                        {
                            session.Buffer.Append(line, kind, stageIndex);
                        }
                        session.Buffer.PartialLine = null;
                        lineBuf.SetLength(0);
                        owner.LiveRenderer.Update(session, session.TerminalOut);
                        continue;
                    }

                    // Handle \r: distinguish between \r\n (normal Windows line
                    // ending) and bare \r (in-place overwrite used by progress
                    // bars, spinners, and F# interactive prompts).
                    //
                    // Peek ahead: if the very next byte in this chunk is \n,
                    // this is a \r\n pair — just put the \r into lineBuf and
                    // let the \n handler above commit the line normally.
                    // StripTrailingCr will strip the \r before it reaches the
                    // buffer. Only bare \r (not followed by \n) triggers the
                    // in-place overwrite path.
                    if (b == (byte)'\r')
                    {
                        // Peek ahead: if the next byte in this chunk is \n,
                        // this is a \r\n pair — a normal line ending, not an
                        // in-place overwrite. Put the \r in lineBuf and let
                        // the \n handler commit the line normally.
                        bool nextIsLf = (i + 1 < read) && buffer[i + 1] == (byte)'\n';
                        if (nextIsLf)
                        {
                            lineBuf.WriteByte(b);
                            continue;
                        }

                        // Edge case: \r is the last byte in this read chunk.
                        // We can't tell if \n follows in the next chunk, so
                        // buffer the \r and defer. If \n arrives next, the
                        // \n handler strips it via StripTrailingCr. If new
                        // text arrives instead, FormatBodyLine strips
                        // everything before the last \r in the rendered line.
                        if (i + 1 >= read)
                        {
                            lineBuf.WriteByte(b);
                            continue;
                        }

                        // Bare \r: genuine carriage return overwrite.
                        if (lineBuf.Length > 0)
                        {
                            string crRaw = Encoding.UTF8.GetString(lineBuf.GetBuffer(), 0, (int)lineBuf.Length);
                            crRaw = StripCursorSequences(crRaw);
                            string crPrefix = ansiTracker.GetStatePrefix();
                            string crLine = crPrefix.Length > 0 ? crPrefix + crRaw : crRaw;

                            if (pendingCr)
                                session.Buffer.ReplaceLast(crLine, kind, stageIndex);
                            else
                                session.Buffer.Append(crLine, kind, stageIndex);

                            session.Buffer.PartialLine = null;
                            pendingCr = true;
                        }
                        lineBuf.SetLength(0);
                        continue;
                    }

                    lineBuf.WriteByte(b);

                    // Update partial line so the renderer can show it immediately (prompts, etc.)
                    string partialRaw = StripTrailingCr(Encoding.UTF8.GetString(lineBuf.GetBuffer(), 0, (int)lineBuf.Length));
                    string pPrefix = ansiTracker.GetStatePrefix();
                    session.Buffer.PartialLine = new BufferLine(pPrefix + partialRaw, kind, stageIndex);
                    owner.LiveRenderer.Update(session, session.TerminalOut);

                    if (r == AltScreenResult.Entered)
                    {
                        int escStart = sniffEscStart;
                        int decValue = sniffer.LastValue;

                        // Pre-ESC content stays in the box buffer as the
                        // last partial line of pre-altscreen output.
                        if (escStart > 0 && escStart <= lineBuf.Length)
                        {
                            string preEsc = StripTrailingCr(Encoding.UTF8.GetString(lineBuf.GetBuffer(), 0, escStart));
                            if (!string.IsNullOrEmpty(preEsc))
                                session.Buffer.Append(preEsc, kind, stageIndex);
                        }
                        lineBuf.SetLength(0);
                        sniffer.Reset();

                        owner.LiveRenderer.EnterAltScreen(session, session.TerminalOut);

                        if (rawOut == null) rawOut = System.Console.OpenStandardOutput();
                        byte[] enterSeq = Encoding.ASCII.GetBytes($"\x1b[?{decValue}h");
                        try
                        {
                            await rawOut.WriteAsync(enterSeq, 0, enterSeq.Length, cancellation).ConfigureAwait(false);
                            if (i + 1 < read)
                                await rawOut.WriteAsync(buffer, i + 1, read - (i + 1), cancellation).ConfigureAwait(false);
                            await rawOut.FlushAsync(cancellation).ConfigureAwait(false);
                        }
                        catch { }

                        break;
                    }
                }

                if (!owner.LiveRenderer.IsAltScreenActive)
                {
                    owner.LiveRenderer.ForceUpdate(session, session.TerminalOut);
                }
            }

            if (lineBuf.Length > 0 && !owner.LiveRenderer.IsAltScreenActive)
            {
                string rawLine = StripTrailingCr(Encoding.UTF8.GetString(lineBuf.GetBuffer(), 0, (int)lineBuf.Length));
                rawLine = StripCursorSequences(rawLine);

                if (kind == LineKind.Stderr && rawLine == "#< CLIXML")
                {
                    return;
                }

                if (kind == LineKind.Stderr && rawLine.StartsWith("<Objs") && rawLine.EndsWith("</Objs>"))
                {
                    var matches = System.Text.RegularExpressions.Regex.Matches(rawLine, @"<S S=""Error"">([^<]*)</S>");
                    foreach (System.Text.RegularExpressions.Match m in matches)
                    {
                        string decoded = System.Text.RegularExpressions.Regex.Replace(m.Groups[1].Value, @"_x([0-9a-fA-F]{4})_", match =>
                            ((char)Convert.ToInt32(match.Groups[1].Value, 16)).ToString());

                        string[] split = decoded.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
                        foreach (var s in split)
                        {
                            if (string.IsNullOrEmpty(s)) continue;
                            session.Buffer.Append(s, kind, stageIndex);
                        }
                    }
                    session.Buffer.PartialLine = null;
                    owner.LiveRenderer.Update(session, session.TerminalOut);
                    return;
                }

                string statePrefix = ansiTracker.GetStatePrefix();
                string line = statePrefix.Length > 0 ? statePrefix + rawLine : rawLine;

                if (pendingCr)
                    session.Buffer.ReplaceLast(line, kind, stageIndex);
                else
                    session.Buffer.Append(line, kind, stageIndex);

                session.Buffer.PartialLine = null;
                owner.LiveRenderer.Update(session, session.TerminalOut);
            }
        }
        catch (OperationCanceledException) { }
        catch (IOException) { }
        catch (System.Exception)
        {
            // we never let a pump task break the shell.
        }
    }

    private static string StripTrailingCr(string s)
    {
        if (s.Length > 0 && s[s.Length - 1] == '\r')
            return s.Substring(0, s.Length - 1);
        return s;
    }

    /// <summary>
    /// Strips cursor-movement and line-erase ANSI escape sequences from a
    /// string before it is committed to the BlackBox buffer. The buffer is
    /// strictly append-only and cannot honour cursor positioning, so these
    /// sequences would appear as garbage in the rendered output if left in.
    ///
    /// Handled sequences:
    ///   ESC[nA  Cursor Up             ESC[nB  Cursor Down
    ///   ESC[nC  Cursor Forward        ESC[nD  Cursor Back
    ///   ESC[nG  Cursor Horizontal Absolute
    ///   ESC[n;mH / ESC[n;mf  Cursor Position
    ///   ESC[nJ  Erase in Display      ESC[nK  Erase in Line
    /// </summary>
    private static string StripCursorSequences(string s)
    {
        if (string.IsNullOrEmpty(s) || s.IndexOf('\x1b') < 0)
            return s;

        var sb = new StringBuilder(s.Length);
        for (int i = 0; i < s.Length; i++)
        {
            if (s[i] == '\x1b' && i + 1 < s.Length && s[i + 1] == '[')
            {
                // Parse CSI: ESC [ <params> <final>
                int j = i + 2;
                // Consume parameter bytes (digits, semicolons, question marks)
                while (j < s.Length && ((s[j] >= '0' && s[j] <= '9') || s[j] == ';' || s[j] == '?'))
                    j++;

                if (j < s.Length)
                {
                    char final = s[j];
                    // Cursor movement and erase sequences to strip
                    if (final == 'A' || final == 'B' || final == 'C' || final == 'D' ||
                        final == 'G' || final == 'H' || final == 'f' ||
                        final == 'J' || final == 'K')
                    {
                        i = j; // skip the entire sequence
                        continue;
                    }
                }
            }

            sb.Append(s[i]);
        }

        return sb.ToString();
    }

    /// <summary>
    /// Possible outcomes after feeding one byte into <see cref="AltScreenSniffer"/>.
    /// </summary>
    private enum AltScreenResult
    {
        None,
        Entered,
    }

    /// <summary>
    /// Tiny state machine that watches a byte stream for DECSET sequences
    /// that enter the terminal's alternate screen buffer:
    ///   ESC [ ? 47 h       (xterm legacy)
    ///   ESC [ ? 1047 h     (xterm clear-on-switch)
    ///   ESC [ ? 1049 h     (xterm save-cursor + clear + switch)
    /// We also remember the byte offset at which the ESC started inside the
    /// caller's accumulation buffer, so the caller can drop the in-flight
    /// sequence and re-emit it cleanly to the real terminal.
    /// </summary>
    private struct AltScreenSniffer
    {
        private int _state;     // 0 idle / 1 ESC / 2 CSI / 3 DECSET-collect
        private int _value;     // accumulated number after '?'
        private int _escStart;  // offset in caller's buf where ESC started

        public int EscStartIdx => _escStart;
        public int LastValue => _value;

        public void Reset()
        {
            _state = 0;
            _value = 0;
            _escStart = 0;
        }

        public AltScreenResult Feed(byte b, int callerBufLen)
        {
            switch (_state)
            {
                case 0:
                    if (b == 0x1b) { _state = 1; _escStart = callerBufLen; }
                    break;
                case 1:
                    if (b == (byte)'[') _state = 2;
                    else _state = 0;
                    break;
                case 2:
                    if (b == (byte)'?') { _state = 3; _value = 0; }
                    else _state = 0;
                    break;
                case 3:
                    if (b >= (byte)'0' && b <= (byte)'9')
                    {
                        _value = _value * 10 + (b - (byte)'0');
                    }
                    else if (b == (byte)'h')
                    {
                        _state = 0;
                        if (_value == 1049 || _value == 1047 || _value == 47)
                            return AltScreenResult.Entered;
                    }
                    else
                    {
                        _state = 0;
                    }
                    break;
            }
            return AltScreenResult.None;
        }
    }

    /// <summary>
    /// Forwards terminal stdin to a child process. Operates in two modes:
    ///
    /// 1. NORMAL MODE (before alt-screen): Uses Console.ReadKey to intercept
    ///    individual keystrokes and forward them. Adequate for simple interactive
    ///    commands that only need basic text input.
    ///
    /// 2. RAW MODE (after alt-screen entry): Puts the terminal into raw mode
    ///    and pumps raw bytes from Console.OpenStandardInput() directly to the
    ///    child's stdin. This preserves all escape sequences (arrow keys,
    ///    function keys, mouse events, etc.) that TUI programs need.
    ///
    /// The mode switch is driven by the LiveRenderer's IsAltScreenActive flag,
    /// which is set by the output pump when it detects DECSET 1049/1047/47.
    /// </summary>
    private static async Task ForwardStdinAsync(
        Process process,
        BlackBoxSession session,
        BlackBoxLiveRenderer liveRenderer,
        CancellationToken cancel)
    {
        TerminalRawMode? rawMode = null;
        var inputBuilder = new StringBuilder();
        int cursor = 0;

        try
        {
            using var stdin = process.StandardInput;
            stdin.AutoFlush = true;

            while (!cancel.IsCancellationRequested && !process.HasExited)
            {
                if (liveRenderer.IsAltScreenActive)
                {
                    // Switch to raw byte pumping for full TUI support.
                    if (rawMode == null)
                    {
                        rawMode = new TerminalRawMode();
                        rawMode.Enter();
                    }

                    await PumpRawStdinAsync(process, rawMode, liveRenderer, cancel).ConfigureAwait(false);
                    return;
                }

                if (!System.Console.IsInputRedirected)
                {
                    if (!System.Console.KeyAvailable)
                    {
                        try { await Task.Delay(30, cancel).ConfigureAwait(false); } catch { return; }
                        continue;
                    }

                    System.ConsoleKeyInfo key;
                    try { key = System.Console.ReadKey(intercept: true); }
                    catch { return; }

                    if (key.Key == System.ConsoleKey.Enter)
                    {
                        string line = inputBuilder.ToString();
                        inputBuilder.Clear();
                        cursor = 0;
                        session.UpdateInput("", 0);

                        try
                        {
                            await stdin.WriteLineAsync(line).ConfigureAwait(false);
                            session.Buffer.Append(line, LineKind.StdinEcho);
                            liveRenderer.ForceUpdate(session, session.TerminalOut);
                        }
                        catch { return; }
                    }
                    else if (key.Key == System.ConsoleKey.Backspace)
                    {
                        if (cursor > 0)
                        {
                            cursor--;
                            inputBuilder.Remove(cursor, 1);
                            session.UpdateInput(inputBuilder.ToString(), cursor);
                            liveRenderer.ForceUpdate(session, session.TerminalOut);
                        }
                    }
                    else if (key.Key == System.ConsoleKey.Delete)
                    {
                        if (cursor < inputBuilder.Length)
                        {
                            inputBuilder.Remove(cursor, 1);
                            session.UpdateInput(inputBuilder.ToString(), cursor);
                            liveRenderer.ForceUpdate(session, session.TerminalOut);
                        }
                    }
                    else if (key.Key == System.ConsoleKey.LeftArrow)
                    {
                        if (cursor > 0)
                        {
                            cursor--;
                            session.UpdateInput(inputBuilder.ToString(), cursor);
                            liveRenderer.ForceUpdate(session, session.TerminalOut);
                        }
                    }
                    else if (key.Key == System.ConsoleKey.RightArrow)
                    {
                        if (cursor < inputBuilder.Length)
                        {
                            cursor++;
                            session.UpdateInput(inputBuilder.ToString(), cursor);
                            liveRenderer.ForceUpdate(session, session.TerminalOut);
                        }
                    }
                    else if (key.Key == System.ConsoleKey.Home)
                    {
                        cursor = 0;
                        session.UpdateInput(inputBuilder.ToString(), cursor);
                        liveRenderer.ForceUpdate(session, session.TerminalOut);
                    }
                    else if (key.Key == System.ConsoleKey.End)
                    {
                        cursor = inputBuilder.Length;
                        session.UpdateInput(inputBuilder.ToString(), cursor);
                        liveRenderer.ForceUpdate(session, session.TerminalOut);
                    }
                    else if (key.KeyChar != '\0' && !char.IsControl(key.KeyChar))
                    {
                        inputBuilder.Insert(cursor, key.KeyChar);
                        cursor++;
                        session.UpdateInput(inputBuilder.ToString(), cursor);
                        liveRenderer.ForceUpdate(session, session.TerminalOut);
                    }
                }
                else
                {
                    int next;
                    try { next = System.Console.In.Read(); }
                    catch { return; }
                    if (next < 0) return;
                    try { await stdin.WriteAsync(((char)next).ToString()).ConfigureAwait(false); } catch { return; }
                }
            }
        }
        catch { }
        finally
        {
            rawMode?.Dispose();
        }
    }

    /// <summary>
    /// Pumps raw bytes from the terminal's standard input directly to the
    /// child process's stdin stream. Runs until the process exits, the
    /// cancellation token fires, or the alt-screen session ends.
    ///
    /// This bypasses .NET's Console.ReadKey entirely and reads the underlying
    /// input stream byte-by-byte, preserving all escape sequences, arrow
    /// keys, function keys, mouse reports, and any other terminal-level
    /// input that TUI programs require.
    /// </summary>
    private static async Task PumpRawStdinAsync(
        Process process,
        TerminalRawMode rawMode,
        BlackBoxLiveRenderer liveRenderer,
        CancellationToken cancel)
    {
        Stream? rawStdin = null;
        try
        {
            rawStdin = System.Console.OpenStandardInput();
            byte[] buffer = new byte[1024];

            while (!cancel.IsCancellationRequested && !process.HasExited)
            {
                int bytesRead;
                try
                {
                    var readTask = rawStdin.ReadAsync(buffer, 0, buffer.Length, cancel);

                    // Use a short timeout so we can periodically check process
                    // state without blocking forever on stdin.
                    var completed = await Task.WhenAny(
                        readTask,
                        Task.Delay(100, cancel)
                    ).ConfigureAwait(false);

                    if (completed != readTask)
                        continue;

                    bytesRead = await readTask.ConfigureAwait(false);
                }
                catch (OperationCanceledException) { return; }
                catch { return; }

                if (bytesRead <= 0) return;

                try
                {
                    await process.StandardInput.BaseStream.WriteAsync(
                        buffer, 0, bytesRead, cancel).ConfigureAwait(false);
                    await process.StandardInput.BaseStream.FlushAsync(cancel).ConfigureAwait(false);
                }
                catch { return; }
            }
        }
        catch (OperationCanceledException) { }
        catch { }
    }
}

public readonly struct BoxRedirectFlags
{
    public bool StdoutRedirected { get; init; }
    public bool StderrRedirected { get; init; }
    public bool StdinRedirected { get; init; }

    public static BoxRedirectFlags None => default;
}
