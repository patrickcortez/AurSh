using System.Text;

namespace AurShell.Core;

public static class Banner
{
    private const string Version = "2.0.0";

    // Block-character letter forms for "AurSh" using Unicode block elements.
    // Each letter is 8 columns wide, 6 rows tall, joined by 2-space gaps.
    // Total width: 48 characters (5×8 letters + 4×2 gaps).
    private static readonly string[] BlockArt = new[]
    {
        @"  ▄██▄    ██    ██  ██▀▀▀█▄    ▄████▄   ██      ",
        @" ██  ██   ██    ██  ██   ▀██  ██    ▀▀  ██      ",
        @"██    ██  ██    ██  █████▀     ▀████▄   ██▄▄▄█▄ ",
        @"████████  ██    ██  ██▀▀█▄    ▄▄    ██  ██▀▀  ██",
        @"██    ██  ▀██  ██▀  ██   ▀█▄  ▀▀   ▄██  ██    ██",
        @"▀▀    ▀▀   ▀████▀   ▀▀    ▀▀   ▀████▀   ▀▀    ▀▀",
    };

    // Rounded Unicode border glyphs
    private const string BorderTopLeft = "\u256d";
    private const string BorderTopRight = "\u256e";
    private const string BorderBottomLeft = "\u2570";
    private const string BorderBottomRight = "\u256f";
    private const string BorderHorizontal = "\u2500";
    private const string BorderVertical = "\u2502";

    private static readonly (int R, int G, int B)[] GradientStops = new[]
    {
        (140, 120, 255),
        (120, 160, 255),
        (100, 200, 240),
        (80, 220, 200),
        (100, 240, 160),
    };

    public static void Print(ShellEnvironment env)
    {
        string? bannerSetting = env.Get("AURSH_BANNER");
        if (string.Equals(bannerSetting, "off", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(bannerSetting, "false", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(bannerSetting, "0", StringComparison.Ordinal))
            return;

        var sb = new StringBuilder();
        sb.AppendLine();

        // Determine the widest content line
        int maxArtLen = 0;
        foreach (string line in BlockArt)
        {
            if (line.Length > maxArtLen)
                maxArtLen = line.Length;
        }

        string accentFg = GetOsAccentFg();
        string dimFg = Utils.Ansi.FgRgb(100, 100, 130);
        string brightFg = Utils.Ansi.FgRgb(180, 200, 255);
        string borderFg = Utils.Ansi.FgRgb(80, 80, 120);

        // Build the info line to measure its width
        string versionText = $"v{Version}";
        string osText = $"{Utils.Platform.OsIcon} {Utils.Platform.OsName}";
        string userHostText = $"{Utils.Platform.UserName}@{Utils.Platform.HostName}";
        string infoLine = $"  {versionText}  \u2502  {osText}  \u2502  {userHostText}";
        int infoLen = infoLine.Length;

        // Box width: max of art and info line, plus side padding
        int contentWidth = System.Math.Max(maxArtLen + 4, infoLen + 4);
        int termWidth = Utils.Platform.TerminalWidth;
        if (contentWidth > termWidth - 4)
            contentWidth = System.Math.Max(20, termWidth - 4);

        // ── Top border ──
        sb.Append("  ");
        sb.Append(borderFg);
        sb.Append(BorderTopLeft);
        sb.Append(RepeatChar('\u2500', contentWidth));
        sb.Append(BorderTopRight);
        sb.Append(Utils.Ansi.Reset);
        sb.AppendLine();

        // ── Art rows ──
        for (int row = 0; row < BlockArt.Length; row++)
        {
            string line = BlockArt[row];
            var (r, g, b) = InterpolateGradient(row, BlockArt.Length - 1);

            sb.Append("  ");
            sb.Append(borderFg);
            sb.Append(BorderVertical);
            sb.Append(Utils.Ansi.Reset);
            sb.Append(' ');

            int artPadLeft = (contentWidth - 2 - line.Length) / 2;
            int artPadRight = contentWidth - 2 - line.Length - artPadLeft;
            if (artPadLeft < 0) artPadLeft = 0;
            if (artPadRight < 0) artPadRight = 0;

            sb.Append(' ', artPadLeft);

            for (int col = 0; col < line.Length; col++)
            {
                char c = line[col];
                if (c == ' ')
                {
                    sb.Append(' ');
                    continue;
                }

                float colRatio = line.Length > 1 ? (float)col / (line.Length - 1) : 0f;
                int cr = Clamp((int)(r + (colRatio * 40) - 20));
                int cg = Clamp((int)(g + (colRatio * 30) - 15));
                int cb = Clamp((int)(b - (colRatio * 30) + 15));

                sb.Append(Utils.Ansi.FgRgb(cr, cg, cb));
                sb.Append(Utils.Ansi.Bold);
                sb.Append(c);
            }
            sb.Append(Utils.Ansi.Reset);

            sb.Append(' ', artPadRight);
            sb.Append(' ');
            sb.Append(borderFg);
            sb.Append(BorderVertical);
            sb.Append(Utils.Ansi.Reset);
            sb.AppendLine();
        }

        // ── Separator line inside the box ──
        sb.Append("  ");
        sb.Append(borderFg);
        sb.Append(BorderVertical);
        sb.Append(Utils.Ansi.Reset);
        sb.Append(' ');
        sb.Append(dimFg);
        int sepLen = System.Math.Min(contentWidth - 2, termWidth - 8);
        sb.Append(RepeatChar('\u2500', sepLen));
        sb.Append(Utils.Ansi.Reset);
        int sepPad = contentWidth - 2 - sepLen;
        if (sepPad > 0) sb.Append(' ', sepPad);
        sb.Append(' ');
        sb.Append(borderFg);
        sb.Append(BorderVertical);
        sb.Append(Utils.Ansi.Reset);
        sb.AppendLine();

        // ── Info line: version │ OS │ user@host ──
        sb.Append("  ");
        sb.Append(borderFg);
        sb.Append(BorderVertical);
        sb.Append(Utils.Ansi.Reset);
        sb.Append(' ');

        var infoBuilder = new StringBuilder();
        infoBuilder.Append(' ');
        infoBuilder.Append(accentFg);
        infoBuilder.Append(Utils.Ansi.Bold);
        infoBuilder.Append('v');
        infoBuilder.Append(Version);
        infoBuilder.Append(Utils.Ansi.Reset);
        infoBuilder.Append(dimFg);
        infoBuilder.Append("  \u2502  ");
        infoBuilder.Append(Utils.Ansi.Reset);
        infoBuilder.Append(brightFg);
        infoBuilder.Append(Utils.Platform.OsIcon);
        infoBuilder.Append(' ');
        infoBuilder.Append(Utils.Platform.OsName);
        infoBuilder.Append(Utils.Ansi.Reset);
        infoBuilder.Append(dimFg);
        infoBuilder.Append("  \u2502  ");
        infoBuilder.Append(Utils.Ansi.Reset);
        infoBuilder.Append(brightFg);
        infoBuilder.Append(Utils.Platform.UserName);
        infoBuilder.Append('@');
        infoBuilder.Append(Utils.Platform.HostName);
        infoBuilder.Append(Utils.Ansi.Reset);
        string infoContent = infoBuilder.ToString();

        sb.Append(infoContent);
        int infoVisLen = Utils.Ansi.VisibleLength(infoContent);
        int infoPad = System.Math.Max(0, contentWidth - 2 - infoVisLen);
        if (infoPad > 0) sb.Append(' ', infoPad);
        sb.Append(' ');
        sb.Append(borderFg);
        sb.Append(BorderVertical);
        sb.Append(Utils.Ansi.Reset);
        sb.AppendLine();

        // ── Help line ──
        sb.Append("  ");
        sb.Append(borderFg);
        sb.Append(BorderVertical);
        sb.Append(Utils.Ansi.Reset);
        sb.Append(' ');

        var helpBuilder = new StringBuilder();
        helpBuilder.Append(' ');
        helpBuilder.Append(dimFg);
        helpBuilder.Append("Type ");
        helpBuilder.Append(Utils.Ansi.Reset);
        helpBuilder.Append(accentFg);
        helpBuilder.Append("help");
        helpBuilder.Append(Utils.Ansi.Reset);
        helpBuilder.Append(dimFg);
        helpBuilder.Append(" for commands, ");
        helpBuilder.Append(Utils.Ansi.Reset);
        helpBuilder.Append(accentFg);
        helpBuilder.Append("exit");
        helpBuilder.Append(Utils.Ansi.Reset);
        helpBuilder.Append(dimFg);
        helpBuilder.Append(" to quit.");
        helpBuilder.Append(Utils.Ansi.Reset);
        string helpContent = helpBuilder.ToString();

        sb.Append(helpContent);
        int helpVisLen = Utils.Ansi.VisibleLength(helpContent);
        int helpPad = System.Math.Max(0, contentWidth - 2 - helpVisLen);
        if (helpPad > 0) sb.Append(' ', helpPad);
        sb.Append(' ');
        sb.Append(borderFg);
        sb.Append(BorderVertical);
        sb.Append(Utils.Ansi.Reset);
        sb.AppendLine();

        // ── About line ──
        sb.Append("  ");
        sb.Append(borderFg);
        sb.Append(BorderVertical);
        sb.Append(Utils.Ansi.Reset);
        sb.Append(' ');

        var aboutBuilder = new StringBuilder();
        aboutBuilder.Append(' ');
        aboutBuilder.Append(dimFg);
        aboutBuilder.Append("Type ");
        aboutBuilder.Append(Utils.Ansi.Reset);
        aboutBuilder.Append(accentFg);
        aboutBuilder.Append("aursh-about");
        aboutBuilder.Append(Utils.Ansi.Reset);
        aboutBuilder.Append(dimFg);
        aboutBuilder.Append(" to learn about Aurshell");
        aboutBuilder.Append(Utils.Ansi.Reset);
        string aboutContent = aboutBuilder.ToString();

        sb.Append(aboutContent);
        int aboutVisLen = Utils.Ansi.VisibleLength(aboutContent);
        int aboutPad = System.Math.Max(0, contentWidth - 2 - aboutVisLen);
        if (aboutPad > 0) sb.Append(' ', aboutPad);
        sb.Append(' ');
        sb.Append(borderFg);
        sb.Append(BorderVertical);
        sb.Append(Utils.Ansi.Reset);
        sb.AppendLine();

        // ── Bottom border ──
        sb.Append("  ");
        sb.Append(borderFg);
        sb.Append(BorderBottomLeft);
        sb.Append(RepeatChar('\u2500', contentWidth));
        sb.Append(BorderBottomRight);
        sb.Append(Utils.Ansi.Reset);
        sb.AppendLine();
        sb.AppendLine();

        Console.Write(sb.ToString());
    }

    private static string RepeatChar(char c, int count)
    {
        if (count <= 0) return "";
        return new string(c, count);
    }

    private static (int R, int G, int B) InterpolateGradient(int index, int maxIndex)
    {
        if (maxIndex <= 0 || index <= 0)
            return GradientStops[0];
        if (index >= maxIndex)
            return GradientStops[GradientStops.Length - 1];

        float t = (float)index / maxIndex * (GradientStops.Length - 1);
        int lower = (int)t;
        int upper = Math.Min(lower + 1, GradientStops.Length - 1);
        float frac = t - lower;

        var a = GradientStops[lower];
        var b = GradientStops[upper];

        return (
            Clamp((int)(a.R + (b.R - a.R) * frac)),
            Clamp((int)(a.G + (b.G - a.G) * frac)),
            Clamp((int)(a.B + (b.B - a.B) * frac))
        );
    }

    private static int Clamp(int value)
    {
        if (value < 0) return 0;
        if (value > 255) return 255;
        return value;
    }

    private static string GetOsAccentFg()
    {
        return Utils.Platform.CurrentOS switch
        {
            Utils.OperatingSystemType.Windows => Utils.Ansi.FgRgb(100, 180, 255),
            Utils.OperatingSystemType.MacOS => Utils.Ansi.FgRgb(200, 200, 210),
            Utils.OperatingSystemType.Linux => Utils.Ansi.FgRgb(240, 190, 80),
            Utils.OperatingSystemType.Termux => Utils.Ansi.FgRgb(100, 210, 120),
            _ => Utils.Ansi.FgRgb(180, 180, 200)
        };
    }
}