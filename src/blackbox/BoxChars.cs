namespace AurShell.BlackBoxView;

public enum BoxStyle
{
    Rounded,
    Square,
    Ascii
}

public sealed class BoxGlyphs
{
    public string TopLeft { get; init; } = "";
    public string TopRight { get; init; } = "";
    public string BottomLeft { get; init; } = "";
    public string BottomRight { get; init; } = "";
    public string Horizontal { get; init; } = "";
    public string Vertical { get; init; } = "";
}

public static class BoxChars
{
    public static readonly BoxGlyphs Rounded = new()
    {
        TopLeft = "\u256d",
        TopRight = "\u256e",
        BottomLeft = "\u2570",
        BottomRight = "\u256f",
        Horizontal = "\u2500",
        Vertical = "\u2502"
    };

    public static readonly BoxGlyphs Square = new()
    {
        TopLeft = "\u250c",
        TopRight = "\u2510",
        BottomLeft = "\u2514",
        BottomRight = "\u2518",
        Horizontal = "\u2500",
        Vertical = "\u2502"
    };

    public static readonly BoxGlyphs Ascii = new()
    {
        TopLeft = "+",
        TopRight = "+",
        BottomLeft = "+",
        BottomRight = "+",
        Horizontal = "-",
        Vertical = "|"
    };

    public static BoxGlyphs From(BoxStyle style) => style switch
    {
        BoxStyle.Square => Square,
        BoxStyle.Ascii => Ascii,
        _ => Rounded
    };

    public static BoxStyle ParseStyle(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return BoxStyle.Rounded;

        return value.Trim().ToLowerInvariant() switch
        {
            "square" or "sharp" => BoxStyle.Square,
            "ascii" or "plain" or "fallback" => BoxStyle.Ascii,
            _ => BoxStyle.Rounded
        };
    }

    public static BoxStyle Detect(string? configured)
    {
        if (!string.IsNullOrWhiteSpace(configured))
            return ParseStyle(configured);

        string? term = System.Environment.GetEnvironmentVariable("TERM");
        bool terminalLooksPlain = term != null && (term.Equals("linux", System.StringComparison.OrdinalIgnoreCase)
                                                   || term.Equals("dumb", System.StringComparison.OrdinalIgnoreCase));
        if (terminalLooksPlain)
            return BoxStyle.Ascii;

        if (Utils.Platform.CurrentOS == Utils.OperatingSystemType.Termux)
            return BoxStyle.Square;

        if (Utils.Platform.CurrentOS == Utils.OperatingSystemType.Windows)
        {
            bool windowsUtf8 = false;
            try { windowsUtf8 = System.Console.OutputEncoding.CodePage == 65001; }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"aursh error: {ex.Message}"); }
            bool windowsTerminal = !string.IsNullOrEmpty(System.Environment.GetEnvironmentVariable("WT_SESSION"))
                                || !string.IsNullOrEmpty(System.Environment.GetEnvironmentVariable("ConEmuPID"));
            if (windowsUtf8 || windowsTerminal)
                return BoxStyle.Rounded;
            return BoxStyle.Ascii;
        }

        string? lang = System.Environment.GetEnvironmentVariable("LANG")
                       ?? System.Environment.GetEnvironmentVariable("LC_ALL")
                       ?? System.Environment.GetEnvironmentVariable("LC_CTYPE");
        bool utf8 = lang != null && lang.ToUpperInvariant().Contains("UTF-8");
        if (!utf8)
            return BoxStyle.Ascii;

        return BoxStyle.Rounded;
    }
}
