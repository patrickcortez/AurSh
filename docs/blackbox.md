# BlackBox

BlackBox is AurSh's TUI execution viewport. Every command's stdin /
stdout / stderr is rendered inside a Unicode-bordered box drawn beneath
the prompt:

```
> prompt

╭─ BlackBox :: python ─────────────────────╮
│ Python 3.14.0                            │
│ >>> print("Hello AurSh")                 │
│ Hello AurSh                              │
│ >>> exit()                               │
╰─────────────── exit: 0 ──────────────────╯

> prompt
```

The box auto-fits the terminal width and scrolls vertically when output
exceeds its visible height. After a command finishes, its box stays in
the scroll history as inert text and the next prompt is printed below.

---

## Status

**BlackBox** is still a work in progress, It's still having issues rendering processes 
that takes over the terminal with their own screen buffer. As of now the only fix it has
is its `bypass.txt` which excludes any processes written in that file from running inside blackbox

---

## Configuration

**BlackBox** is also Configurable via environment variables (read once on shell start):

| Variable                       | Default                                   | Meaning |
|--------------------------------|-------------------------------------------|---------| 
| `BLACKBOX_BORDER`              | `rounded` (auto-falls back per terminal)  | Border glyphs: `rounded`, `square`, `ascii` |
| `BLACKBOX_MAX_HEIGHT`          | `min(20, WindowHeight - 4)`               | Cap on visible body rows |
| `BLACKBOX_MIN_HEIGHT`          | `1`                                       | Minimum body rows |
| `BLACKBOX_BUFFER_LINES`        | `5000`                                    | Per-box scrollback line cap |
| `BLACKBOX_SHOW_PIPE_INTERIOR`  | `0`                                       | Show intermediate pipeline stages in body (M1+) |
| `BLACKBOX_IN_SCRIPTS`          | `0`                                       | Render boxes inside `.aur` script execution |
| `BLACKBOX_BYPASS`              | `vim nvim vi nano less more man top htop btop fzf tmux screen ssh` | Additional TUI programs to bypass (appends to dynamic detection) |

Auto-detected fallbacks:

- Non-UTF-8 `LANG` / `LC_ALL` / `LC_CTYPE`: `ascii` border.
- `TERM=linux` or `TERM=dumb`: `ascii` border.
- Termux: `square` border (the rounded glyphs don't render reliably in
  every Termux font).

---

## Style gallery

```
rounded:
╭─ BlackBox :: echo ──────────────╮
│ hi                              │
╰─────────── exit: 0 ─────────────╯

square:
┌─ BlackBox :: echo ──────────────┐
│ hi                              │
└─────────── exit: 0 ─────────────┘

ascii:
+- BlackBox :: echo ------------- +
| hi                              |
+----------- exit: 0 ------------ +
```

---

## Architecture

```
src/blackbox/
  BlackBox.cs               Facade; Open()/Repaint(); owns active session
  BlackBoxConfig.cs         Env-var-driven settings; capability detection
  BlackBoxSession.cs        Per-command IDisposable state (id, command,
                            start/finish, exit code, buffer, TerminalOut)
  BlackBoxBuffer.cs         Ring of typed body lines (stdout, stderr,
                            stdin echo, meta); scroll window
  BlackBoxRenderer.cs       Paints header / body / footer to a TextWriter
  BlackBoxLiveRenderer.cs   Throttled in-place repaint (cursor-up + erase)
  BlackBoxWriter.cs         TextWriter that appends to the session buffer
  BlackBoxIo.cs             PrepareForBox + PumpAsync (line-aware byte pump)
                            + alt-screen detection + raw stdin forwarding
  BypassList.cs             Detect fullscreen-TUI commands to bypass
  PtyHost.cs                `script -qfec` wrapper for interactive children
  TerminalRawMode.cs        Cross-platform raw terminal mode for alt-screen
  BoxChars.cs               Rounded / square / ascii glyph sets + detect
  BlackBoxDemo.cs           Renders sample scenes for the demo builtin
```

### Execution flow

1. `Shell.Run` reads a line. If the head word is in `BLACKBOX_BYPASS`
   the command runs raw without a box.
2. Otherwise `BlackBox.Open(commandLine)` creates a `BlackBoxSession`
   and `BlackBoxLiveRenderer.Start` paints the initial "running" frame.
3. `Pipeline.Execute` runs:
   - **Builtins** retarget `Console.Out`/`Error` to a `BlackBoxWriter`
     so `Console.WriteLine` appends straight into the session buffer.
   - **Single externals** redirect stdio via `BlackBoxIo.PrepareForBox`
     and pump it through `BlackBoxIo.PumpAsync`. Interactive commands
     (python, ssh, mysql, ...) on POSIX get re-wrapped through
     `script -qfec` for proper TTY semantics.
   - **Pipelines** keep their natural stdin/stdout chain, but the last
     stage's stdout and every stage's stderr are additionally pumped
     into the session buffer. Per-stage lines are prefixed `[N]`.
4. As bytes arrive, `BlackBoxLiveRenderer.Update` rewinds the cursor
   over the previous render, clears, and repaints. Throttled to 33ms.
5. On exit, the footer flips from `running` to `exit: N` and the
   cursor is left below the box.

---

## Alt-screen takeover

Some programs use the terminal's alternate screen buffer to draw their
own full-screen interface (vim, less, htop, fzf, etc.). The terminal
switches between two independent screen buffers using DEC Private Mode
sequences:

| Sequence          | Meaning |
|-------------------|---------|
| `ESC[?1049h`     | Enter alternate screen (save cursor + clear + switch) |
| `ESC[?1047h`     | Enter alternate screen (clear on switch) |
| `ESC[?47h`       | Enter alternate screen (legacy) |
| `ESC[?1049l`     | Leave alternate screen (restore cursor + switch back) |

---

BlackBox handles alternate screen programs in three tiers:

### Tier 1: Bypass (zero interception)

Bypassed programs run completely outside the BlackBox. No session is
opened, no stdio is redirected. The process inherits the terminal's raw
file descriptors and has full control.

Bypass is determined from **three sources**, checked in order:

#### Source 1 — Static TUI list (`BLACKBOX_BYPASS`)

Well-known TUI programs that can't function in a box:
`vim`, `nvim`, `vi`, `nano`, `less`, `more`, `man`, `top`, `htop`,
`btop`, `fzf`, `tmux`, `screen`, `ssh`.

Override via environment variable:
```bash
export BLACKBOX_BYPASS="vim nvim nano less fzf tmux ssh mycustomtui"
```

#### Source 2 — User bypass file (`~/.aursh/bypass.txt`)

A persistent, user-editable file. One program name per line, `#` for
comments. Survives shell restarts without needing env vars:

```bash
# ~/.aursh/bypass.txt
# My custom TUI programs
mycustomtui
my-repl
/usr/local/bin/special-shell
```

Both basenames (`mytool`) and full paths (`/usr/bin/mytool`) are
accepted — full paths are reduced to their basename for matching.

#### Source 3 — Dynamic shell discovery (automatic)

At first use, AurShell discovers all shells installed on the system:

| Platform     | Discovery method |
|--------------|------------------|
| Linux / macOS | Reads `/etc/shells` — the POSIX-standard file listing every valid login shell. Every entry's basename is extracted and cached. |
| Termux       | Reads both `/etc/shells` and `$PREFIX/etc/shells`. |
| Windows      | Probes PATH for common shell executables (`cmd`, `powershell`, `pwsh`, `wsl`, `bash`, `zsh`, `fish`, etc.). Also scans Git for Windows, MSYS2, and Cygwin install paths. |
| All          | Honors `$SHELL` (POSIX) and `%COMSPEC%` (Windows) environment variables. |

This means **newly installed shells are picked up automatically** on
the next AurSh startup — no configuration needed.

Example: installing `fish` on Ubuntu adds `/usr/bin/fish` to
`/etc/shells`. The next time AurSh starts, `fish` is automatically
bypassed.

### Tier 2: Passthrough mode (header + raw body + footer)

Interactive REPLs on Windows (where no PTY wrapper exists) run in
passthrough mode: the BlackBox header is printed, then the process
inherits the real terminal stdio for its body output, and the footer
is printed after the process exits.

### Tier 3: Dynamic alt-screen detection (automatic)

For commands not in the bypass list, BlackBox intercepts stdio and
pumps output through the box renderer. If the process later emits a
DECSET alternate screen sequence, BlackBox detects this and switches
to a takeover mode:

1. **Output sniffing**: `BlackBoxIo.PumpToBufferAsync` contains an
   `AltScreenSniffer` state machine that watches for `ESC[?1049h`,
   `ESC[?1047h`, or `ESC[?47h` byte sequences in the stdout stream.

2. **Renderer transition**: When detected, any pre-altscreen output
   already buffered continues to live in the box. The
   `BlackBoxLiveRenderer.EnterAltScreen()` method is called, which
   flushes pending body rows and suspends further box painting.

3. **Raw stdout forwarding**: All subsequent stdout bytes are forwarded
   directly to the real terminal via `Console.OpenStandardOutput()`,
   bypassing the box renderer entirely. The alt-screen enter sequence
   itself is also forwarded so the terminal actually switches buffers.

4. **Raw stdin forwarding**: The stdin forwarder (`ForwardStdinAsync`)
   detects the alt-screen state and switches from `Console.ReadKey`
   interception to raw byte pumping via `Console.OpenStandardInput()`.
   A `TerminalRawMode` instance puts the terminal into raw mode
   (disabling echo, line buffering, and processed input) so that
   escape sequences, arrow keys, function keys, mouse events, and all
   other terminal-level input pass through unmodified to the child
   process.

5. **Exit cleanup**: When the child process exits, `Finish()` or
   `Abort()` emits `ESC[?1049l` to leave the alternate screen buffer
   and switch back to the main buffer where the box header and body
   rows live. The footer is then rendered below the box. Terminal
   attributes are restored via `TerminalRawMode.Dispose()`.

```
┌──────────────────────────────────────────────────────────┐
│                    Command starts                        │
│                         │                                │
│                    ┌────▼────┐                            │
│                    │ Bypass? │                            │
│                    └────┬────┘                            │
│              ┌──yes─────┴─────no──┐                      │
│              │                    │                       │
│         Raw terminal        BlackBox session              │
│         (no box)            stdio redirected              │
│                                   │                       │
│                          ┌────────▼────────┐              │
│                          │ Alt-screen       │              │
│                          │ detected?        │              │
│                          └────────┬────────┘              │
│                      ┌──no───────┴──────yes──┐           │
│                      │                       │            │
│                 Normal box              Switch to         │
│                 rendering               raw forwarding    │
│                 (stdout→buffer→box)     (stdout→terminal) │
│                                         (stdin→raw bytes) │
│                                         (terminal→raw)    │
│                                              │            │
│                                         Child exits       │
│                                              │            │
│                                         Leave alt-screen  │
│                                         Restore terminal  │
│                                         Render footer     │
└──────────────────────────────────────────────────────────┘
```

### TerminalRawMode

Cross-platform terminal mode management for alt-screen takeover:

| Platform | Mechanism |
|----------|-----------|
| POSIX    | Saves terminal attributes via `stty -g`, then applies `stty raw -echo icrnl`. Restores saved attributes on exit, or falls back to `stty sane`. |
| Windows  | Uses `kernel32.dll` `GetConsoleMode`/`SetConsoleMode`. Disables `ENABLE_LINE_INPUT`, `ENABLE_ECHO_INPUT`, `ENABLE_PROCESSED_INPUT`. Enables `ENABLE_WINDOW_INPUT` and `ENABLE_VIRTUAL_TERMINAL_INPUT`. Restores saved mode on exit. |

## Roadmap

- **M0** — Visual prototype, hidden demo builtin. **DONE**.
- **M1** — Wire `BlackBox.Open` into `Shell.Run`; tee builtin
  Console.Out and external process stdio into the active session;
  pipeline tee semantics; bypass list for TUI programs. **DONE**.
- **M2** — PTY-backed mode for interactive children (python REPL,
  `apt install`, `read -s`, etc.). **DONE** on POSIX via `/usr/bin/script`.
  Native Pty.Net / ConPTY support for Windows + ANSI cursor parser
  remains future work.
- **M3** — Alt-screen takeover; SIGWINCH redraw; adaptive layout tiers;
  TerminalRawMode for full TUI forwarding. **DONE**.
- **M4** — Plugin hooks (`OnBoxOpen`, `OnBoxClose`) for lua / F#.

## Adaptive layout tiers

BlackBox picks one of three layout densities based on terminal width.
The tier is recomputed live whenever the terminal resizes (rotation,
soft-keyboard show/hide, pinch-zoom, window drag) via SIGWINCH on POSIX
and a 750ms polling fallback on every platform.

| Tier      | Width range  | Look                                                      |
|-----------|--------------|-----------------------------------------------------------|
| `Full`    | `>= 60 cols` | Full box: header + side borders + footer.                 |
| `Compact` | `30..59`     | Header + footer only; body has no `│` side borders.       |
| `Bar`     | `< 30 cols`  | Single-line `▸` header + raw body + single-line `└` footer. |

Size detection runs a 5-stage cascade and takes the first sane result:

1. `ioctl(TIOCGWINSZ)` via `Console.WindowWidth`/`Height` (~µs).
2. `COLUMNS` / `LINES` env vars (set by some emulators after resize).
3. `stty size` (POSIX standard fallback).
4. `tput cols` / `tput lines` (terminfo).
5. `termux-tty-size` (Termux only, from `termux-tools`).

Stale-state safety: if all probes fail, the last known good size is
retained — we never silently fall back to `80×24` mid-session.

Subscribers (currently the live renderer) are notified on a background
thread after a 60ms debounce that coalesces orientation-flip bursts.
Rows already past the viewport stay at their old width — we cannot
retroactively rewrite terminal scrollback. Rows still inside the
viewport reflow on the next emit.

## PTY (M2) configuration

On POSIX, BlackBox wraps known-interactive commands with
`/usr/bin/script -qfec` so they see a real TTY and render prompts /
colors / progress bars correctly.

Default interactive list includes: `python*`, `ipython*`, `node`,
`deno`, `ruby`, `irb`, `perl`, `lua`, `luajit`, `ghci`, `scala`,
`ssh`, `telnet`, `ftp`, `sftp`, `mysql`, `mariadb`, `psql`, `sqlite3`,
`redis-cli`, `mongo`, `mongosh`, `gdb`, `lldb`, `pdb`, `racket`,
`clojure`, `guile`, `swipl`.

Override with environment variables:

| Variable        | Effect |
|-----------------|--------|
| `AURSH_PTY`     | Comma/space-separated extra command names to PTY-wrap |
| `AURSH_NO_PTY`  | Set to `1` to disable PTY wrapping entirely |
| `AURSH_DISABLE_PTY` | Set to `1` to force `PtyHost.IsAvailable()` to false on POSIX (routes interactive commands to passthrough mode instead) |

Windows currently falls back to plain pipes (no ConPTY yet).
