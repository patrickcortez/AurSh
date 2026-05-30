# BlackBox

**What it does**
BlackBox is AurSh's visual execution viewport. When you run a command, instead of the output just scrolling wildly down your terminal, BlackBox captures the text and displays it cleanly inside a bordered box beneath your prompt. It automatically adapts to your terminal's width and keeps your session history organized.

**Example Usage**
You don't need to do anything special to use it—just run any standard command.
```bash
> python
╭─ BlackBox :: python ─────────────────────╮
│ Python 3.14.0                            │
│ >>> print("Hello AurSh")                 │
│ Hello AurSh                              │
│ >>> exit()                               │
╰─────────────── exit: 0 ──────────────────╯
```

**How it works internally**
1. When you type a command, AurSh checks if the program is meant to take over your whole screen (like `vim` or `nano`). If it is, BlackBox steps aside.
2. If it's a normal command, BlackBox spins up an internal session buffer and redirects the command's standard input and output streams into this buffer.
3. A background thread redraws the terminal box in place 30 times a second, creating a smooth visual experience as text streams in.
4. When the command finishes, BlackBox updates its footer with the exit code and leaves the box preserved in your scroll history.

---

## Configuration

**What it does**
You can configure the style, dimensions, and behavior of BlackBox via environment variables.

**Example Usage**
You can add these to your configuration scripts:
```bash
# Change the box to use square borders instead of rounded
export BLACKBOX_BORDER="square"

# Bypass BlackBox for your custom terminal app
export BLACKBOX_BYPASS="my_custom_tui"
```

## Alt-Screen Takeover & Bypassing

**What it does**
Some applications (like text editors or `htop`) need full control of the terminal to draw their UI. BlackBox automatically detects these programs and gets out of their way.

**How it works internally**
BlackBox handles this in a few ways:
1. **Static Lists**: It checks your `BLACKBOX_BYPASS` variable and your `~/.aursh/bypass.txt` file for known full-screen apps.
2. **Dynamic Shell Discovery**: On startup, AurSh scans your system for installed shells (like `bash` or `fish`) and automatically bypasses them.
3. **Live Sequence Sniffing**: As a command runs, BlackBox watches the raw bytes coming out. If the program emits a special "alternate screen" code (e.g. `ESC[?1049h`), BlackBox instantly stops drawing the box, hands full raw control over to the program, and restores itself only when the program quits.

---

## Adaptive Layouts

**What it does**
BlackBox automatically shifts its visual style depending on how much horizontal space you have on your screen, ensuring it always looks readable on both wide desktop monitors and narrow phone screens (like Termux).

**How it works internally**
1. It constantly polls your terminal's window size.
2. **Full Tier (>60 columns)**: Draws full borders around all sides.
3. **Compact Tier (30-59 columns)**: Removes the side borders to save space.
4. **Bar Tier (<30 columns)**: Reduces the box to a single-line top and bottom bar.
