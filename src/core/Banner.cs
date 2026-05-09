using System.Text;

namespace AurShell.Core;

public static class Banner
{
    private const string Version = "1.1.0";

    private static readonly string[] AsciiArt = new[]
    {
        @"     _                 ____  _     ",
        @"    / \  _   _ _ __  / ___|| |__  ",
        @"   / _ \| | | | '__| \___ \| '_ \ ",
        @"  / ___ \ |_| | |     ___) | | | |",
        @" /_/   \_\__,_|_|    |____/|_| |_|",
    };

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

        int maxLen = 0;
        foreach (string line in AsciiArt)
        {
            if (line.Length > maxLen)
                maxLen = line.Length;
        }

        for (int row = 0; row < AsciiArt.Length; row++)
        {
            string line = AsciiArt[row];
            var (r, g, b) = InterpolateGradient(row, AsciiArt.Length - 1);

            sb.Append("  ");
            for (int col = 0; col < line.Length; col++)
            {
                char c = line[col];
                if (c == ' ')
                {
                    sb.Append(' ');
                    continue;
                }

                float colRatio = maxLen > 1 ? (float)col / (maxLen - 1) : 0f;
                int cr = Clamp((int)(r + (colRatio * 40) - 20));
                int cg = Clamp((int)(g + (colRatio * 30) - 15));
                int cb = Clamp((int)(b - (colRatio * 30) + 15));

                sb.Append(Utils.Ansi.FgRgb(cr, cg, cb));
                sb.Append(Utils.Ansi.Bold);
                sb.Append(c);
            }

            sb.Append(Utils.Ansi.Reset);
            sb.AppendLine();
        }

        sb.AppendLine();

        string accentFg = GetOsAccentFg();
        string dimFg = Utils.Ansi.FgRgb(100, 100, 130);
        string brightFg = Utils.Ansi.FgRgb(180, 200, 255);

        sb.Append("  ");
        sb.Append(accentFg);
        sb.Append(Utils.Ansi.Bold);
        sb.Append("  v");
        sb.Append(Version);
        sb.Append(Utils.Ansi.Reset);
        sb.Append(dimFg);
        sb.Append("  \u2502  ");
        sb.Append(Utils.Ansi.Reset);
        sb.Append(brightFg);
        sb.Append(Utils.Platform.OsIcon);
        sb.Append(' ');
        sb.Append(Utils.Platform.OsName);
        sb.Append(Utils.Ansi.Reset);
        sb.Append(dimFg);
        sb.Append("  \u2502  ");
        sb.Append(Utils.Ansi.Reset);
        sb.Append(brightFg);
        sb.Append(Utils.Platform.UserName);
        sb.Append('@');
        sb.Append(Utils.Platform.HostName);
        sb.Append(Utils.Ansi.Reset);
        sb.AppendLine();

        sb.Append("  ");
        sb.Append(dimFg);
        int infoLineLen = 4 + Version.Length + 5 + Utils.Platform.OsName.Length + 5 +
                          Utils.Platform.UserName.Length + 1 + Utils.Platform.HostName.Length + 2;
        sb.Append(new string('\u2500', Math.Min(infoLineLen, Utils.Platform.TerminalWidth - 4)));
        sb.Append(Utils.Ansi.Reset);
        sb.AppendLine();

        sb.Append("  ");
        sb.Append(dimFg);
        sb.Append("  Type ");
        sb.Append(Utils.Ansi.Reset);
        sb.Append(accentFg);
        sb.Append("help");
        sb.Append(Utils.Ansi.Reset);
        sb.Append(dimFg);
        sb.Append(" for commands, ");
        sb.Append(Utils.Ansi.Reset);
        sb.Append(accentFg);
        sb.Append("exit");
        sb.Append(Utils.Ansi.Reset);
        sb.Append(dimFg);
        sb.Append(" to quit.");
        sb.Append(Utils.Ansi.Reset);
        sb.Append(dimFg);
        sb.Append("\n   Type ");
        sb.Append(Utils.Ansi.Reset);
        sb.Append(accentFg);
        sb.Append("aursh-about ");
        sb.Append(Utils.Ansi.Reset);
        sb.Append(dimFg);
        sb.Append("to learn about Aurshell");
        sb.Append(Utils.Ansi.Reset);
        sb.AppendLine();
        sb.AppendLine();

        Console.Write(sb.ToString());
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