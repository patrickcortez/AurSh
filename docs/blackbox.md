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

This is the M0 visual prototype. The renderer, buffer, and config
surface are in place; runtime integration with `Executor` and
`Pipeline` (so every command actually runs inside a box) lands in M1.

You can preview the rendering today by running the hidden
`aursh-blackbox-demo` builtin:

```bash
aursh-blackbox-demo
aursh-blackbox-demo square
aursh-blackbox-demo ascii
```

It paints six sample scenes (empty box, python REPL, pipeline, stderr
interleaved, overflow with scroll indicator, style fallbacks).

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

## Architecture (M0)

```
src/blackbox/
  BlackBox.cs             Facade; Open()/Repaint(); owns active session
  BlackBoxConfig.cs       Env-var-driven settings; capability detection
  BlackBoxSession.cs      Per-command IDisposable state (id, command,
                          start/finish, exit code, buffer)
  BlackBoxBuffer.cs       Ring of typed body lines (stdout, stderr,
                          stdin echo, meta); scroll window
  BlackBoxRenderer.cs     Paints header / body / footer to a TextWriter
  BoxChars.cs             Rounded / square / ascii glyph sets + detect
  BlackBoxDemo.cs         Renders sample scenes for the demo builtin
```

The M0 renderer paints a static block of text to `Console.Out` (or any
`TextWriter`). It does **not** anchor to a row or repaint in place yet;
that arrives in M1 along with the actual I/O capture pipeline.

## Roadmap

- **M0** — Visual prototype, hidden demo builtin (this milestone).
- **M1** — Wire `BlackBox.Open` into `Shell.Run`; tee builtin
  Console.Out and external process stdio into the active session;
  pipeline tee semantics; bypass list for TUI programs.
- **M2** — Pty-backed mode for interactive programs (python REPL,
  `apt install`, `read -s`, etc.); ANSI parser for in-place updates.
- **M3** — Scrollback navigation after a command finishes; SIGWINCH
  redraw; theme polish.
- **M4** — Plugin hooks (`OnBoxOpen`, `OnBoxClose`) for lua / F#.
