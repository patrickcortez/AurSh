using System.Runtime.InteropServices;

namespace AurShell.BlackBoxView;

/// <summary>
/// Cross-platform terminal raw mode management for alt-screen takeover.
/// When a child process needs full terminal control (vim, less, htop, etc.),
/// the terminal must be put into raw mode so that escape sequences, arrow
/// keys, function keys, and all control characters pass through unmodified
/// to the child via stdin forwarding.
///
/// On POSIX: uses stty to save/restore terminal attributes.
/// On Windows: uses kernel32 SetConsoleMode to toggle ENABLE_LINE_INPUT,
///             ENABLE_ECHO_INPUT, and ENABLE_PROCESSED_INPUT.
/// </summary>
public sealed class TerminalRawMode : System.IDisposable
{
    private bool _disposed;
    private bool _active;

    // POSIX: saved stty state
    private string? _savedStty;

    // Windows: saved console mode
    private uint _savedConsoleMode;
    private bool _windowsModeWasSaved;

    // Windows console mode constants
    private const uint ENABLE_PROCESSED_INPUT = 0x0001;
    private const uint ENABLE_LINE_INPUT = 0x0002;
    private const uint ENABLE_ECHO_INPUT = 0x0004;
    private const uint ENABLE_WINDOW_INPUT = 0x0008;
    private const uint ENABLE_VIRTUAL_TERMINAL_INPUT = 0x0200;
    private const int STD_INPUT_HANDLE = -10;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern System.IntPtr GetStdHandle(int nStdHandle);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetConsoleMode(System.IntPtr hConsoleHandle, out uint lpMode);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetConsoleMode(System.IntPtr hConsoleHandle, uint dwMode);

    public bool IsActive => _active;

    public void Enter()
    {
        if (_active) return;

        if (Utils.Platform.CurrentOS == Utils.OperatingSystemType.Windows)
            EnterWindows();
        else
            EnterPosix();

        _active = true;
    }

    public void Exit()
    {
        if (!_active) return;
        _active = false;

        if (Utils.Platform.CurrentOS == Utils.OperatingSystemType.Windows)
            ExitWindows();
        else
            ExitPosix();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Exit();
    }

    private void EnterPosix()
    {
        try
        {
            var savePsi = new System.Diagnostics.ProcessStartInfo("stty")
            {
                Arguments = "-g",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var saveProc = System.Diagnostics.Process.Start(savePsi);
            if (saveProc != null)
            {
                _savedStty = saveProc.StandardOutput.ReadToEnd().Trim();
                saveProc.WaitForExit();
            }
        }
        catch
        {
            _savedStty = null;
        }

        try
        {
            var rawPsi = new System.Diagnostics.ProcessStartInfo("stty")
            {
                Arguments = "raw -echo icrnl",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var rawProc = System.Diagnostics.Process.Start(rawPsi);
            rawProc?.WaitForExit();
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"aursh error: {ex.Message}"); }
    }

    private void ExitPosix()
    {
        if (string.IsNullOrEmpty(_savedStty))
        {
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo("stty")
                {
                    Arguments = "sane",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var proc = System.Diagnostics.Process.Start(psi);
                proc?.WaitForExit();
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"aursh error: {ex.Message}"); }
            return;
        }

        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo("stty")
            {
                Arguments = _savedStty,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var proc = System.Diagnostics.Process.Start(psi);
            proc?.WaitForExit();
        }
        catch
        {
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo("stty")
                {
                    Arguments = "sane",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var proc = System.Diagnostics.Process.Start(psi);
                proc?.WaitForExit();
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"aursh error: {ex.Message}"); }
        }
    }

    private void EnterWindows()
    {
        try
        {
            System.IntPtr handle = GetStdHandle(STD_INPUT_HANDLE);
            if (handle == System.IntPtr.Zero || handle == new System.IntPtr(-1))
                return;

            if (!GetConsoleMode(handle, out uint mode))
                return;

            _savedConsoleMode = mode;
            _windowsModeWasSaved = true;

            uint rawMode = mode;
            rawMode &= ~ENABLE_LINE_INPUT;
            rawMode &= ~ENABLE_ECHO_INPUT;
            rawMode &= ~ENABLE_PROCESSED_INPUT;
            rawMode |= ENABLE_WINDOW_INPUT;
            rawMode |= ENABLE_VIRTUAL_TERMINAL_INPUT;

            SetConsoleMode(handle, rawMode);
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"aursh error: {ex.Message}"); }
    }

    private void ExitWindows()
    {
        if (!_windowsModeWasSaved) return;
        try
        {
            System.IntPtr handle = GetStdHandle(STD_INPUT_HANDLE);
            if (handle != System.IntPtr.Zero && handle != new System.IntPtr(-1))
                SetConsoleMode(handle, _savedConsoleMode);
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"aursh error: {ex.Message}"); }
        _windowsModeWasSaved = false;
    }
}
