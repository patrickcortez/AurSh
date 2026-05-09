using System.Text;

namespace AurShell.Core;

public class Prompt
{
    private readonly ShellEnvironment _env;
    private readonly Utils.GitInfo _gitInfo = new();

    private const string BoxTopLeft = "\u256D\u2500";
    private const string BoxBottomLeft = "\u2570\u2500";
    private const string PowerlineSep = "\uE0B0";

    private const string DefaultLine1Format = "{box_top} {os_badge}{powerline}{user_host}{powerline}{dir_badge}{git}{status}";
    private const string DefaultLine2Format = "{box_bottom} {chevron} ";

    public Prompt(ShellEnvironment env)
    {
        _env = env;
    }

    public string Render(string workingDirectory, int lastExitCode)
    {
        _gitInfo.Refresh(workingDirectory);

        int termWidth = Utils.Platform.TerminalWidth;
        var sb = new StringBuilder();

        string line1Format = _env.Get("AURSH_PROMPT") ?? DefaultLine1Format;
        string line2Format = _env.Get("AURSH_PROMPT2") ?? DefaultLine2Format;

        string line1 = RenderLine(line1Format, workingDirectory, lastExitCode, termWidth, true);
        string line2 = RenderLine(line2Format, workingDirectory, lastExitCode, termWidth, false);

        sb.Append(line1);
        sb.Append('\n');
        sb.Append(line2);

        return sb.ToString();
    }

    public int PromptVisibleLength(int lastExitCode)
    {
        string line2Format = _env.Get("AURSH_PROMPT2") ?? DefaultLine2Format;
        string line2 = RenderLine(line2Format, "", lastExitCode, 80, false);
        return Utils.Ansi.VisibleLength(line2);
    }

    private string RenderLine(string format, string workingDirectory, int lastExitCode, int termWidth, bool isLine1)
    {
        var sb = new StringBuilder();
        int i = 0;

        while (i < format.Length)
        {
            if (format[i] == '{')
            {
                int close = format.IndexOf('}', i + 1);
                if (close > i)
                {
                    string token = format.Substring(i + 1, close - i - 1).Trim().ToLowerInvariant();
                    sb.Append(ExpandToken(token, workingDirectory, lastExitCode));
                    i = close + 1;
                    continue;
                }
            }

            sb.Append(format[i]);
            i++;
        }

        if (isLine1)
        {
            string leftPart = sb.ToString();
            int leftVisible = Utils.Ansi.VisibleLength(leftPart);

            string timeSegment = BuildTimeSegment(DateTime.Now.ToString("HH:mm:ss"));
            int timeVisible = Utils.Ansi.VisibleLength(timeSegment);

            int gap = termWidth - leftVisible - timeVisible;
            if (gap > 0)
            {
                sb.Append(new string(' ', gap));
                sb.Append(timeSegment);
            }
        }

        return sb.ToString();
    }

    private string ExpandToken(string token, string workingDirectory, int lastExitCode)
    {
        return token switch
        {
            "box_top" => $"{Utils.Ansi.FgBrightBlack}{BoxTopLeft}",
            "box_bottom" => $"{Utils.Ansi.FgBrightBlack}{BoxBottomLeft}",
            "os_badge" => BuildOsBadge(),
            "powerline" => BuildPowerlineTransition(),
            "user_host" => BuildUserHostSegment(),
            "dir_badge" or "dir" => BuildDirSegment(workingDirectory),
            "git" => BuildGitSegment(),
            "status" => BuildStatusIndicator(lastExitCode),
            "chevron" => BuildChevron(lastExitCode),
            "time" => BuildTimeSegment(DateTime.Now.ToString("HH:mm:ss")),
            "user" => Utils.Platform.UserName,
            "host" => Utils.Platform.HostName,
            "cwd" => Utils.Platform.ShortenPath(workingDirectory),
            "cwd_full" => workingDirectory,
            "os_icon" => Utils.Platform.OsIcon,
            "os_name" => Utils.Platform.OsName,
            "newline" or "nl" => "\n",
            "reset" => Utils.Ansi.Reset,
            "bold" => Utils.Ansi.Bold,
            "dim" => Utils.Ansi.Dim,
            "exit_code" => lastExitCode.ToString(),
            "shell" => "aursh",
            "version" => "1.2.0",
            "dollar" => System.Environment.UserName == "root" ? "#" : "$",
            "arrow" => "\u276F",
            "lambda" => "\u03BB",
            "branch" => _gitInfo.IsGitRepo ? _gitInfo.FormatStatus() : "",
            _ => ExpandColorToken(token)
        };
    }

    private string ExpandColorToken(string token)
    {
        if (token.StartsWith("fg:"))
        {
            string colorSpec = token.Substring(3);
            return ParseColorSpec(colorSpec, false);
        }
        if (token.StartsWith("bg:"))
        {
            string colorSpec = token.Substring(3);
            return ParseColorSpec(colorSpec, true);
        }
        return "{" + token + "}";
    }

    private static string ParseColorSpec(string spec, bool isBackground)
    {
        string[] parts = spec.Split(',');
        if (parts.Length == 3 &&
            int.TryParse(parts[0].Trim(), out int r) &&
            int.TryParse(parts[1].Trim(), out int g) &&
            int.TryParse(parts[2].Trim(), out int b))
        {
            return isBackground ? Utils.Ansi.BgRgb(r, g, b) : Utils.Ansi.FgRgb(r, g, b);
        }

        return spec.ToLowerInvariant() switch
        {
            "red" => isBackground ? Utils.Ansi.BgRed : Utils.Ansi.FgRed,
            "green" => isBackground ? Utils.Ansi.BgGreen : Utils.Ansi.FgGreen,
            "blue" => isBackground ? Utils.Ansi.BgBlue : Utils.Ansi.FgBlue,
            "yellow" => isBackground ? Utils.Ansi.BgYellow : Utils.Ansi.FgYellow,
            "magenta" => isBackground ? Utils.Ansi.BgMagenta : Utils.Ansi.FgMagenta,
            "cyan" => isBackground ? Utils.Ansi.BgCyan : Utils.Ansi.FgCyan,
            "white" => isBackground ? Utils.Ansi.BgWhite : Utils.Ansi.FgWhite,
            "black" => isBackground ? Utils.Ansi.BgBlack : Utils.Ansi.FgBlack,
            "bright_red" => isBackground ? Utils.Ansi.BgBrightRed : Utils.Ansi.FgBrightRed,
            "bright_green" => isBackground ? Utils.Ansi.BgBrightGreen : Utils.Ansi.FgBrightGreen,
            "bright_blue" => isBackground ? Utils.Ansi.BgBrightBlue : Utils.Ansi.FgBrightBlue,
            "bright_yellow" => isBackground ? Utils.Ansi.BgBrightYellow : Utils.Ansi.FgBrightYellow,
            "bright_cyan" => isBackground ? Utils.Ansi.BgBrightCyan : Utils.Ansi.FgBrightCyan,
            "bright_magenta" => isBackground ? Utils.Ansi.BgBrightMagenta : Utils.Ansi.FgBrightMagenta,
            "bright_white" => isBackground ? Utils.Ansi.BgBrightWhite : Utils.Ansi.FgBrightWhite,
            "default" => isBackground ? Utils.Ansi.BgDefault : Utils.Ansi.FgDefault,
            _ => ""
        };
    }

    private string BuildOsBadge()
    {
        var sb = new StringBuilder();
        string osBg = GetOsBgColor();

        sb.Append(osBg);
        sb.Append(Utils.Ansi.FgWhite);
        sb.Append(Utils.Ansi.Bold);
        sb.Append(' ');
        sb.Append(Utils.Platform.OsIcon);
        sb.Append(' ');
        sb.Append(Utils.Ansi.Reset);

        return sb.ToString();
    }

    private string BuildPowerlineTransition()
    {
        return "";
    }

    private string BuildUserHostSegment()
    {
        var sb = new StringBuilder();
        string osBg = GetOsBgColor();

        sb.Append(Utils.Ansi.FgFromBg(osBg));
        sb.Append(Utils.Ansi.BgRgb(50, 50, 70));
        sb.Append(PowerlineSep);

        sb.Append(Utils.Ansi.BgRgb(50, 50, 70));
        sb.Append(Utils.Ansi.FgRgb(180, 210, 255));
        sb.Append(' ');
        sb.Append(Utils.Platform.UserName);
        sb.Append(Utils.Ansi.FgRgb(100, 100, 130));
        sb.Append('@');
        sb.Append(Utils.Ansi.FgRgb(140, 170, 220));
        sb.Append(Utils.Platform.HostName);
        sb.Append(' ');
        sb.Append(Utils.Ansi.Reset);

        return sb.ToString();
    }

    private string BuildDirSegment(string workingDirectory)
    {
        var sb = new StringBuilder();
        string dir = Utils.Platform.ShortenPath(workingDirectory);

        sb.Append(Utils.Ansi.FgRgb(50, 50, 70));
        sb.Append(Utils.Ansi.BgRgb(40, 40, 55));
        sb.Append(PowerlineSep);

        sb.Append(Utils.Ansi.BgRgb(40, 40, 55));
        sb.Append(Utils.Ansi.FgRgb(120, 200, 255));
        sb.Append(" \uF115 ");
        sb.Append(dir);
        sb.Append(' ');
        sb.Append(Utils.Ansi.Reset);

        return sb.ToString();
    }

    private string BuildGitSegment()
    {
        if (!_gitInfo.IsGitRepo)
        {
            var noGit = new StringBuilder();
            noGit.Append(Utils.Ansi.FgRgb(40, 40, 55));
            noGit.Append(PowerlineSep);
            noGit.Append(Utils.Ansi.Reset);
            return noGit.ToString();
        }

        var sb = new StringBuilder();
        string gitFg = _gitInfo.IsDirty ? Utils.Ansi.FgRgb(255, 170, 100) : Utils.Ansi.FgRgb(130, 230, 150);
        string gitBg = Utils.Ansi.BgRgb(30, 30, 45);

        sb.Append(Utils.Ansi.FgRgb(40, 40, 55));
        sb.Append(gitBg);
        sb.Append(PowerlineSep);

        sb.Append(gitBg);
        sb.Append(gitFg);
        sb.Append(' ');
        sb.Append(_gitInfo.FormatStatus());
        sb.Append(' ');
        sb.Append(Utils.Ansi.Reset);

        sb.Append(Utils.Ansi.FgRgb(30, 30, 45));
        sb.Append(PowerlineSep);
        sb.Append(Utils.Ansi.Reset);

        return sb.ToString();
    }

    private string BuildStatusIndicator(int lastExitCode)
    {
        if (lastExitCode == 0)
            return "";

        var sb = new StringBuilder();
        sb.Append(Utils.Ansi.FgRgb(255, 100, 100));
        sb.Append(" \u2718 ");
        sb.Append(lastExitCode);
        sb.Append(Utils.Ansi.Reset);
        return sb.ToString();
    }

    private string BuildChevron(int lastExitCode)
    {
        var sb = new StringBuilder();
        if (lastExitCode == 0)
        {
            sb.Append(Utils.Ansi.FgRgb(100, 230, 150));
            sb.Append('\u276F');
        }
        else
        {
            sb.Append(Utils.Ansi.FgRgb(255, 100, 100));
            sb.Append('\u276F');
        }
        sb.Append(Utils.Ansi.Reset);
        return sb.ToString();
    }

    private string BuildTimeSegment(string time)
    {
        var sb = new StringBuilder();

        sb.Append(Utils.Ansi.FgRgb(80, 80, 100));
        sb.Append('\uF43A');
        sb.Append(' ');
        sb.Append(Utils.Ansi.FgRgb(130, 130, 160));
        sb.Append(time);
        sb.Append(Utils.Ansi.Reset);

        return sb.ToString();
    }

    private string GetOsBgColor()
    {
        return Utils.Platform.CurrentOS switch
        {
            Utils.OperatingSystemType.Windows => Utils.Ansi.BgRgb(0, 90, 170),
            Utils.OperatingSystemType.MacOS => Utils.Ansi.BgRgb(90, 90, 100),
            Utils.OperatingSystemType.Linux => Utils.Ansi.BgRgb(180, 130, 30),
            Utils.OperatingSystemType.Termux => Utils.Ansi.BgRgb(40, 140, 55),
            _ => Utils.Ansi.BgRgb(60, 60, 60)
        };
    }
}
