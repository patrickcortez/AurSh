using AurShell.Utils;

namespace AurShell.BlackBoxView;

public sealed class BlackBoxConfig
{
    public BoxStyle Border { get; set; } = BoxStyle.Rounded;
    public string BorderColor { get; set; } = Ansi.FgBrightBlack;
    public string TitleColor { get; set; } = Ansi.FgBrightCyan;
    public string MetaColor { get; set; } = Ansi.FgBrightBlack;
    public string StderrColor { get; set; } = Ansi.FgBrightRed;
    public string ExitOkColor { get; set; } = Ansi.FgBrightGreen;
    public string ExitErrColor { get; set; } = Ansi.FgBrightRed;

    public int? MaxHeight { get; set; }
    public int MinHeight { get; set; } = 1;
    public int BufferLines { get; set; } = 5000;

    public bool ShowPipeInterior { get; set; }
    public bool InScripts { get; set; }

    public List<string> Bypass { get; set; } = new()
    {
        "vim", "nvim", "vi", "nano",
        "less", "more", "man",
        "top", "htop", "btop",
        "fzf", "tmux", "screen", "ssh"
    };

    public static BlackBoxConfig FromEnvironment()
    {
        var c = new BlackBoxConfig
        {
            Border = BoxChars.Detect(System.Environment.GetEnvironmentVariable("BLACKBOX_BORDER")),
            MaxHeight = TryParseInt(System.Environment.GetEnvironmentVariable("BLACKBOX_MAX_HEIGHT")),
            BufferLines = TryParseInt(System.Environment.GetEnvironmentVariable("BLACKBOX_BUFFER_LINES")) ?? 5000,
            ShowPipeInterior = ParseBool(System.Environment.GetEnvironmentVariable("BLACKBOX_SHOW_PIPE_INTERIOR")),
            InScripts = ParseBool(System.Environment.GetEnvironmentVariable("BLACKBOX_IN_SCRIPTS"))
        };

        int? minHeight = TryParseInt(System.Environment.GetEnvironmentVariable("BLACKBOX_MIN_HEIGHT"));
        if (minHeight is int mh && mh > 0)
            c.MinHeight = mh;

        string? bypass = System.Environment.GetEnvironmentVariable("BLACKBOX_BYPASS");
        if (!string.IsNullOrWhiteSpace(bypass))
        {
            c.Bypass = new List<string>(bypass.Split(new[] { ' ', '\t', ',' },
                System.StringSplitOptions.RemoveEmptyEntries));
        }

        return c;
    }

    public int ResolveMaxHeight()
    {
        if (MaxHeight is int explicitMax && explicitMax > 0)
            return System.Math.Max(MinHeight + 2, explicitMax);

        int termHeight = Platform.TerminalHeight;
        int derived = System.Math.Min(20, System.Math.Max(MinHeight + 2, termHeight - 4));
        return derived;
    }

    private static int? TryParseInt(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        return int.TryParse(value, out int v) ? v : null;
    }

    private static bool ParseBool(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        return value.Trim().ToLowerInvariant() switch
        {
            "1" or "true" or "yes" or "on" => true,
            _ => false
        };
    }
}
