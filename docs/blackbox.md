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

## Status

M0 (visual prototype), M1 (wired into every command) and M2 (PTY for
interactive children on POSIX) are shipped.

Every command run in the interactive shell — and every command run via
`aursh --box <cmd>` for one-shot testing — executes inside a BlackBox
viewport: stdout/stderr appear in the box body, exit code in the
footer, the box is live-repainted as output streams in (~30fps), and
fullscreen TUI programs (`vim`, `top`, `less`, `htop`, `tmux`, `ssh`,
etc.) bypass the box entirely.

For a no-side-effect aesthetic preview, the hidden
`aursh-blackbox-demo` builtin still paints six sample scenes:

```bash
aursh-blackbox-demo
aursh-blackbox-demo square
aursh-blackbox-demo ascii
```

## Configuration

Configured via environment variables (read once on shell start):

| Variable                       | Default                                   | Meaning |
|--------------------------------|-------------------------------------------|---------|
| `BLACKBOX_BORDER`              | `rounded` (auto-falls back per terminal)  | Border glyphs: `rounded`, `square`, `ascii` |
| `BLACKBOX_MAX_HEIGHT`          | `min(20, WindowHeight - 4)`               | Cap on visible body rows |
| `BLACKBOX_MIN_HEIGHT`          | `1`                                       | Minimum body rows |
| `BLACKBOX_BUFFER_LINES`        | `5000`                                    | Per-box scrollback line cap |
| `BLACKBOX_SHOW_PIPE_INTERIOR`  | `0`                                       | Show intermediate pipeline stages in body (M1+) |
| `BLACKBOX_IN_SCRIPTS`          | `0`                                       | Render boxes inside `.aur` script execution |
| `BLACKBOX_BYPASS`              | `vim nvim vi nano less more man top htop btop fzf tmux screen ssh` | TUI programs that bypass the box (M1+) |

Auto-detected fallbacks:

- Non-UTF-8 `LANG` / `LC_ALL` / `LC_CTYPE`: `ascii` border.
- `TERM=linux` or `TERM=dumb`: `ascii` border.
- Termux: `square` border (the rounded glyphs don't render reliably in
  every Termux font).

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

## Architecture

```
src/blackbox/
  BlackBox.cs             Facade; Open()/Repaint(); owns active session
  BlackBoxConfig.cs       Env-var-driven settings; capability detection
  BlackBoxSession.cs      Per-command IDisposable state (id, command,
                          start/finish, exit code, buffer, TerminalOut)
  BlackBoxBuffer.cs       Ring of typed body lines (stdout, stderr,
                          stdin echo, meta); scroll window
  BlackBoxRenderer.cs     Paints header / body / footer to a TextWriter
  BlackBoxLiveRenderer.cs Throttled in-place repaint (cursor-up + erase)
  BlackBoxWriter.cs       TextWriter that appends to the session buffer
  BlackBoxIo.cs           PrepareForBox + PumpAsync (line-aware byte pump)
  BypassList.cs           Detect fullscreen-TUI commands to bypass
  PtyHost.cs              `script -qfec` wrapper for interactive children
  BoxChars.cs             Rounded / square / ascii glyph sets + detect
  BlackBoxDemo.cs         Renders sample scenes for the demo builtin
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

## Roadmap

- **M0** — Visual prototype, hidden demo builtin. **DONE**.
- **M1** — Wire `BlackBox.Open` into `Shell.Run`; tee builtin
  Console.Out and external process stdio into the active session;
  pipeline tee semantics; bypass list for TUI programs. **DONE**.
- **M2** — PTY-backed mode for interactive children (python REPL,
  `apt install`, `read -s`, etc.). **DONE** on POSIX via `/usr/bin/script`.
  Native Pty.Net / ConPTY support for Windows + ANSI cursor parser
  remains future work.
- **M3** — Scrollback navigation after a command finishes; SIGWINCH
  redraw; theme polish.
- **M4** — Plugin hooks (`OnBoxOpen`, `OnBoxClose`) for lua / F#.

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

Windows currently falls back to plain pipes (no ConPTY yet).
