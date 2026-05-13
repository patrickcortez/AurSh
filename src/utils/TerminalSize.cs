using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace AurShell.Utils;

/// <summary>
/// Single source of truth for the terminal's current size (columns × rows).
///
/// Why this exists:
///   - <see cref="Console.WindowWidth"/> hits an ioctl on every call. Calling
///     it once per body row (which the BlackBox renderer used to do) is
///     wasteful, and on Android Termux it can intermittently return 0 when
///     the app is backgrounded.
///   - We need to react to live resizes (rotation, soft-keyboard show/hide,
///     pinch-zoom, window drag) without depending solely on SIGWINCH, which
///     Android coalesces or drops in some states.
///
/// Strategy:
///   - Cache (Width, Height). Renderers read the cached value cheaply.
///   - Resolve via a 5-stage cascade: ioctl → COLUMNS/LINES env →
///     `stty size` → `tput cols/lines` → `termux-tty-size` → last known
///     good. The first sane (>= 20×6) result wins.
///   - Subscribe to SIGWINCH on POSIX. Re-detect on receipt.
///   - Also poll every 750ms while there are subscribers — handles cases
///     where SIGWINCH is lost (Android background→foreground, some terminals
///     that don't propagate the signal). Negligible CPU.
///   - Debounce: orientation changes can fire a burst of 3–5 SIGWINCHes
///     within ~60ms; we coalesce them into one Changed event.
/// </summary>
public static class TerminalSize
{
    /// <summary>Last good width. Reads are cheap and lock-free.</summary>
    public static int Width => _width;

    /// <summary>Last good height. Reads are cheap and lock-free.</summary>
    public static int Height => _height;

    /// <summary>
    /// Fires after the terminal size changes (post-debounce). The handler runs
    /// on a background thread; subscribers must be thread-safe.
    /// </summary>
    public static event Action<int, int>? Changed;

    private static volatile int _width = 80;
    private static volatile int _height = 24;
    private static readonly object _sync = new();
    private static Timer? _pollTimer;
    private static Timer? _debounceTimer;
    private static PosixSignalRegistration? _sigwinchReg;
    private static volatile bool _started;
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(750);
    private static readonly TimeSpan DebounceDelay = TimeSpan.FromMilliseconds(60);

    /// <summary>
    /// Idempotent. Initializes the cache, hooks SIGWINCH on POSIX, and starts
    /// the polling timer. Safe to call multiple times.
    /// </summary>
    public static void Start()
    {
        if (_started) return;
        lock (_sync)
        {
            if (_started) return;
            DetectInto(out int w, out int h);
            _width = w;
            _height = h;

            TrySubscribeSigwinch();
            _pollTimer = new Timer(_ => OnTick(), null, PollInterval, PollInterval);
            _started = true;
        }
    }

    /// <summary>
    /// Force a fresh size detection and fire <see cref="Changed"/> if the
    /// new size differs. Useful when something outside this class knows the
    /// terminal just resized (e.g. an explicit user "redraw" command).
    /// </summary>
    public static void ForceRefresh()
    {
        ScheduleSample(immediate: true);
    }

    private static void TrySubscribeSigwinch()
    {
        if (OperatingSystem.IsWindows()) return;
        try
        {
            // PosixSignalRegistration delivers the callback on the thread
            // pool, not in signal-handler context, so it's safe to do work.
            _sigwinchReg = PosixSignalRegistration.Create(PosixSignal.SIGWINCH, _ => ScheduleSample(immediate: false));
        }
        catch
        {
            // Older runtimes / sandboxed environments may reject signal
            // registration. The polling timer is sufficient on its own.
        }
    }

    private static void OnTick()
    {
        // Only sample if someone cares. Avoids unnecessary ioctls when no
        // session is active.
        if (Changed == null) return;
        ScheduleSample(immediate: false);
    }

    private static void ScheduleSample(bool immediate)
    {
        if (immediate)
        {
            Sample();
            return;
        }

        // Debounce: coalesce bursts of resize events into one Sample() call.
        Timer? existing = _debounceTimer;
        if (existing != null)
        {
            try { existing.Change(DebounceDelay, Timeout.InfiniteTimeSpan); return; }
            catch { /* timer was disposed; fall through and create a new one */ }
        }

        _debounceTimer = new Timer(_ => Sample(), null, DebounceDelay, Timeout.InfiniteTimeSpan);
    }

    private static void Sample()
    {
        DetectInto(out int w, out int h);
        int oldW = _width;
        int oldH = _height;
        if (w == oldW && h == oldH) return;
        _width = w;
        _height = h;

        Action<int, int>? handler = Changed;
        if (handler == null) return;
        try { handler.Invoke(w, h); }
        catch { /* swallow subscriber exceptions; never let them break the timer */ }
    }

    /// <summary>
    /// 5-stage cascade. Returns the first sane result; otherwise the last
    /// known good. Never throws.
    /// </summary>
    private static void DetectInto(out int width, out int height)
    {
        // 1. Console.WindowWidth/Height (ioctl(TIOCGWINSZ) on POSIX, native
        //    console APIs on Windows). Common path, ~microseconds.
        try
        {
            int w = Console.WindowWidth;
            int h = Console.WindowHeight;
            if (Sane(w, h)) { width = w; height = h; return; }
        }
        catch { /* fall through */ }

        // 2. Environment variables COLUMNS / LINES. Some terminals export
        //    these but don't propagate ioctl correctly.
        if (TryParseEnv("COLUMNS", out int envW) && TryParseEnv("LINES", out int envH) && Sane(envW, envH))
        {
            width = envW; height = envH; return;
        }

        // 3. `stty size` -> "rows cols"
        if (TryRunForSize("stty", "size", parseSttySize: true, out int sttyW, out int sttyH) && Sane(sttyW, sttyH))
        {
            width = sttyW; height = sttyH; return;
        }

        // 4. `tput cols` and `tput lines` (terminfo).
        if (TryRunForInt("tput", "cols", out int tputW) &&
            TryRunForInt("tput", "lines", out int tputH) &&
            Sane(tputW, tputH))
        {
            width = tputW; height = tputH; return;
        }

        // 5. Termux: `termux-tty-size` prints "<rows> <cols>".
        if (IsTermux() &&
            TryRunForSize("termux-tty-size", "", parseSttySize: true, out int tW, out int tH) &&
            Sane(tW, tH))
        {
            width = tW; height = tH; return;
        }

        // Fall back to last-known good (never reset to 80x24 silently — that
        // masks real failures from showing up in renders).
        width = _width;
        height = _height;
    }

    private static bool Sane(int w, int h) => w >= 20 && h >= 6 && w < 10000 && h < 10000;

    private static bool TryParseEnv(string name, out int value)
    {
        value = 0;
        string? raw = Environment.GetEnvironmentVariable(name);
        return !string.IsNullOrEmpty(raw) && int.TryParse(raw.Trim(), out value) && value > 0;
    }

    private static bool TryRunForInt(string exe, string args, out int value)
    {
        value = 0;
        if (!TryRunCapture(exe, args, out string output)) return false;
        return int.TryParse(output.Trim(), out value) && value > 0;
    }

    private static bool TryRunForSize(string exe, string args, bool parseSttySize, out int w, out int h)
    {
        w = 0; h = 0;
        if (!TryRunCapture(exe, args, out string output)) return false;
        string trimmed = output.Trim();
        if (string.IsNullOrEmpty(trimmed)) return false;

        // `stty size` and `termux-tty-size` both print "<rows> <cols>".
        string[] parts = trimmed.Split(new[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2) return false;
        if (!int.TryParse(parts[0], out int rows)) return false;
        if (!int.TryParse(parts[1], out int cols)) return false;
        if (parseSttySize) { w = cols; h = rows; return true; }
        // Future-proof: if a probe ever prints "cols rows" instead, swap.
        w = rows; h = cols; return true;
    }

    private static bool TryRunCapture(string exe, string args, out string stdout)
    {
        stdout = "";
        try
        {
            var psi = new ProcessStartInfo(exe, args)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            using var proc = Process.Start(psi);
            if (proc == null) return false;
            // 250ms is plenty for these probes; if they hang, give up.
            if (!proc.WaitForExit(250))
            {
                try { proc.Kill(); } catch { }
                return false;
            }
            if (proc.ExitCode != 0) return false;
            stdout = proc.StandardOutput.ReadToEnd();
            return !string.IsNullOrEmpty(stdout);
        }
        catch
        {
            return false;
        }
    }

    private static bool IsTermux()
    {
        // Termux exports PREFIX=/data/data/com.termux/files/usr.
        string? prefix = Environment.GetEnvironmentVariable("PREFIX");
        if (!string.IsNullOrEmpty(prefix) && prefix.Contains("com.termux", StringComparison.OrdinalIgnoreCase))
            return true;
        try { return Directory.Exists("/data/data/com.termux/files/usr"); } catch { return false; }
    }
}
