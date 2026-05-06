using System.Text;
using System.Text.RegularExpressions;

namespace AurShell.Utils;

public static class Ansi
{
    public const string Reset = "\x1b[0m";
    public const string Bold = "\x1b[1m";
    public const string Dim = "\x1b[2m";
    public const string Italic = "\x1b[3m";
    public const string Underline = "\x1b[4m";
    public const string Blink = "\x1b[5m";
    public const string Reverse = "\x1b[7m";
    public const string Hidden = "\x1b[8m";
    public const string Strikethrough = "\x1b[9m";

    public const string BoldOff = "\x1b[22m";
    public const string ItalicOff = "\x1b[23m";
    public const string UnderlineOff = "\x1b[24m";
    public const string BlinkOff = "\x1b[25m";
    public const string ReverseOff = "\x1b[27m";
    public const string HiddenOff = "\x1b[28m";
    public const string StrikethroughOff = "\x1b[29m";

    public const string CursorUp = "\x1b[A";
    public const string CursorDown = "\x1b[B";
    public const string CursorRight = "\x1b[C";
    public const string CursorLeft = "\x1b[D";
    public const string CursorSave = "\x1b[s";
    public const string CursorRestore = "\x1b[u";
    public const string CursorHide = "\x1b[?25l";
    public const string CursorShow = "\x1b[?25h";

    public const string ClearScreen = "\x1b[2J";
    public const string ClearScreenFromCursor = "\x1b[0J";
    public const string ClearLine = "\x1b[2K";
    public const string ClearLineFromCursor = "\x1b[0K";
    public const string ClearLineToStart = "\x1b[1K";

    private static readonly Regex AnsiPattern = new Regex(
        @"\x1b\[[0-9;]*[A-Za-z]|\x1b\]8;[^;]*;[^\x1b]*\x1b\\",
        RegexOptions.Compiled
    );

    public static string Fg256(int color) => $"\x1b[38;5;{color}m";
    public static string Bg256(int color) => $"\x1b[48;5;{color}m";
    public static string FgRgb(int r, int g, int b) => $"\x1b[38;2;{r};{g};{b}m";
    public static string BgRgb(int r, int g, int b) => $"\x1b[48;2;{r};{g};{b}m";

    public static string FgBlack => "\x1b[30m";
    public static string FgRed => "\x1b[31m";
    public static string FgGreen => "\x1b[32m";
    public static string FgYellow => "\x1b[33m";
    public static string FgBlue => "\x1b[34m";
    public static string FgMagenta => "\x1b[35m";
    public static string FgCyan => "\x1b[36m";
    public static string FgWhite => "\x1b[37m";
    public static string FgDefault => "\x1b[39m";

    public static string FgBrightBlack => "\x1b[90m";
    public static string FgBrightRed => "\x1b[91m";
    public static string FgBrightGreen => "\x1b[92m";
    public static string FgBrightYellow => "\x1b[93m";
    public static string FgBrightBlue => "\x1b[94m";
    public static string FgBrightMagenta => "\x1b[95m";
    public static string FgBrightCyan => "\x1b[96m";
    public static string FgBrightWhite => "\x1b[97m";

    public static string BgBlack => "\x1b[40m";
    public static string BgRed => "\x1b[41m";
    public static string BgGreen => "\x1b[42m";
    public static string BgYellow => "\x1b[43m";
    public static string BgBlue => "\x1b[44m";
    public static string BgMagenta => "\x1b[45m";
    public static string BgCyan => "\x1b[46m";
    public static string BgWhite => "\x1b[47m";
    public static string BgDefault => "\x1b[49m";

    public static string BgBrightBlack => "\x1b[100m";
    public static string BgBrightRed => "\x1b[101m";
    public static string BgBrightGreen => "\x1b[102m";
    public static string BgBrightYellow => "\x1b[103m";
    public static string BgBrightBlue => "\x1b[104m";
    public static string BgBrightMagenta => "\x1b[105m";
    public static string BgBrightCyan => "\x1b[106m";
    public static string BgBrightWhite => "\x1b[107m";

    public static string MoveCursorUp(int n) => $"\x1b[{n}A";
    public static string MoveCursorDown(int n) => $"\x1b[{n}B";
    public static string MoveCursorRight(int n) => $"\x1b[{n}C";
    public static string MoveCursorLeft(int n) => $"\x1b[{n}D";
    public static string SetCursorPosition(int row, int col) => $"\x1b[{row};{col}H";
    public static string SetCursorColumn(int col) => $"\x1b[{col}G";
    public static string ScrollUp(int n) => $"\x1b[{n}S";
    public static string ScrollDown(int n) => $"\x1b[{n}T";

    public static string SetTitle(string title) => $"\x1b]0;{title}\x07";

    public static string Hyperlink(string url, string text) =>
        $"\x1b]8;;{url}\x1b\\{text}\x1b]8;;\x1b\\";

    public static string Strip(string text) => AnsiPattern.Replace(text, "");

    public static int VisibleLength(string text) => Strip(text).Length;

    public static string Colorize(string text, string fg) => $"{fg}{text}{Reset}";

    public static string Colorize(string text, string fg, string bg) => $"{fg}{bg}{text}{Reset}";

    public static string PadRight(string text, int totalWidth, char padChar = ' ')
    {
        int visible = VisibleLength(text);
        if (visible >= totalWidth)
            return text;
        return text + new string(padChar, totalWidth - visible);
    }

    public static string PadLeft(string text, int totalWidth, char padChar = ' ')
    {
        int visible = VisibleLength(text);
        if (visible >= totalWidth)
            return text;
        return new string(padChar, totalWidth - visible) + text;
    }

    public static string Center(string text, int totalWidth, char padChar = ' ')
    {
        int visible = VisibleLength(text);
        if (visible >= totalWidth)
            return text;
        int leftPad = (totalWidth - visible) / 2;
        int rightPad = totalWidth - visible - leftPad;
        return new string(padChar, leftPad) + text + new string(padChar, rightPad);
    }

    public static string Segment(string content, string fg, string bg, string nextBg, char separator = '\uE0B0')
    {
        return $"{fg}{bg} {content} {Reset}{Ansi.FgFromBg(bg)}{nextBg}{separator}{Reset}";
    }

    public static string FgFromBg(string bgCode)
    {
        string inner = bgCode.Replace("\x1b[", "").Replace("m", "");

        if (inner.StartsWith("48;5;"))
            return $"\x1b[38;5;{inner.Substring(5)}m";

        if (inner.StartsWith("48;2;"))
            return $"\x1b[38;2;{inner.Substring(5)}m";

        if (int.TryParse(inner, out int code))
        {
            int fgCode = code switch
            {
                >= 40 and <= 47 => code - 10,
                >= 100 and <= 107 => code - 10,
                _ => 39
            };
            return $"\x1b[{fgCode}m";
        }

        return FgDefault;
    }

    public static AnsiBuilder Builder() => new AnsiBuilder();
}

public class AnsiBuilder
{
    private readonly StringBuilder _sb = new StringBuilder();

    public AnsiBuilder Append(string text) { _sb.Append(text); return this; }
    public AnsiBuilder Bold() { _sb.Append(Ansi.Bold); return this; }
    public AnsiBuilder Dim() { _sb.Append(Ansi.Dim); return this; }
    public AnsiBuilder Italic() { _sb.Append(Ansi.Italic); return this; }
    public AnsiBuilder Underline() { _sb.Append(Ansi.Underline); return this; }
    public AnsiBuilder Reset() { _sb.Append(Ansi.Reset); return this; }
    public AnsiBuilder Fg(string code) { _sb.Append(code); return this; }
    public AnsiBuilder Bg(string code) { _sb.Append(code); return this; }
    public AnsiBuilder Fg256(int c) { _sb.Append(Ansi.Fg256(c)); return this; }
    public AnsiBuilder Bg256(int c) { _sb.Append(Ansi.Bg256(c)); return this; }
    public AnsiBuilder FgRgb(int r, int g, int b) { _sb.Append(Ansi.FgRgb(r, g, b)); return this; }
    public AnsiBuilder BgRgb(int r, int g, int b) { _sb.Append(Ansi.BgRgb(r, g, b)); return this; }
    public AnsiBuilder ClearLine() { _sb.Append(Ansi.ClearLine); return this; }
    public AnsiBuilder ClearToEnd() { _sb.Append(Ansi.ClearLineFromCursor); return this; }
    public AnsiBuilder MoveCursorLeft(int n) { _sb.Append(Ansi.MoveCursorLeft(n)); return this; }
    public AnsiBuilder MoveCursorRight(int n) { _sb.Append(Ansi.MoveCursorRight(n)); return this; }
    public AnsiBuilder MoveCursorUp(int n) { _sb.Append(Ansi.MoveCursorUp(n)); return this; }
    public AnsiBuilder MoveCursorDown(int n) { _sb.Append(Ansi.MoveCursorDown(n)); return this; }
    public AnsiBuilder SetColumn(int col) { _sb.Append(Ansi.SetCursorColumn(col)); return this; }
    public AnsiBuilder SaveCursor() { _sb.Append(Ansi.CursorSave); return this; }
    public AnsiBuilder RestoreCursor() { _sb.Append(Ansi.CursorRestore); return this; }
    public AnsiBuilder HideCursor() { _sb.Append(Ansi.CursorHide); return this; }
    public AnsiBuilder ShowCursor() { _sb.Append(Ansi.CursorShow); return this; }

    public override string ToString() => _sb.ToString();
    public int Length => _sb.Length;
    public void Clear() => _sb.Clear();
}
