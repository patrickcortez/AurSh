# AurSh Configuration

**What it does**
AurSh allows you to customize the visual appearance of your terminal prompt. You can change the spacing, line styles, segment edge shapes, and whether the prompt uses a verbose two-line layout or a compact one-line layout.

**Example Usage**
You can configure these settings by editing the `.aursh/AurSh.config.con` file in your home directory:

```ini
[Config]
PromptSpacing=Compressed
PromptLine=none
SegmentEdges=arrow
PromptTheme=dark
Verbose=true
```

- `PromptSpacing`: Choose `Sparse` (adds space between commands) or `Compressed` (no extra spaces).
- `PromptLine`: Choose `dotted`, `line`, or `none` to connect the time segment to the main prompt.
- `SegmentEdges`: Choose `angled`, `arrow`, or `rounded` for the prompt's UI edges.
- `PromptTheme`: Sets the color theme (e.g. light, dark, bright).
- `Verbose`: Choose `true` for a 2-line prompt, or `false` for a 1-line prompt.

**How it works internally**
1. When AurSh starts, it locates and parses the `AurSh.config.con` file.
2. The configuration reader extracts the visual preferences under the `[Config]` section.
3. Every time you press Enter, the internal `Prompt.cs` renderer uses these settings to construct and colorize the prompt text before displaying it on the screen.
