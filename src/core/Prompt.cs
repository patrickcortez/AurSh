using System.Text;

namespace AurShell.Core;

public class Prompt
{
    private readonly ShellEnvironment _env;
    private readonly Utils.GitInfo _gitInfo = new();

    private const string BoxTopLeft = "\u256D\u2500";
    private const string BoxBottomLeft = "\u2570\u2500";

    private const string DefaultLine1Format = "{box_top} {os_badge}{powerline}{user_host}{powerline}{dir_badge}{git}{battery}{network}{status}";
    private const string DefaultLine2Format = "{box_bottom} {chevron} ";

    public Prompt(ShellEnvironment env)
    {
        _env = env;
    }

    private AurShell.Parser.Reader? _reader;
    private string _segmentEdge = "\uE0B0";
    private char _promptLineChar = ' ';
    private bool _isVerbose = true;
    private bool _isSparse = false;

    private void ReloadConfig()
    {
        try
        {
            _reader = new AurShell.Parser.Reader();
            string edge = _reader.GetAttribute("Config", "SegmentEdges")?.ToLowerInvariant() ?? "";
            _segmentEdge = edge switch
            {
                "arrow" => "\uE0B0",
                "rounded" => "\uE0B4",
                "angled" => "\uE0B8",
                _ => "\uE0B0"
            };

            string pLine = _reader.GetAttribute("Config", "PromptLine")?.ToLowerInvariant() ?? "";
            _promptLineChar = pLine switch
            {
                "line" => '\u2500',
                "dotted" => '\u00B7',
                "none" => ' ',
                _ => ' '
            };

            string verbose = _reader.GetAttribute("Config", "Verbose")?.ToLowerInvariant() ?? "";
            _isVerbose = verbose != "false";

            string spacing = _reader.GetAttribute("Config", "PromptSpacing")?.ToLowerInvariant() ?? "";
            _isSparse = spacing == "sparse";
        }
        catch
        {
            // Fallback defaults on read error
            _segmentEdge = "\uE0B0";
            _promptLineChar = ' ';
            _isVerbose = true;
            _isSparse = false;
        }
    }

    public string Render(string workingDirectory, int lastExitCode)
    {
        ReloadConfig();
        _gitInfo.Refresh(workingDirectory);

        int termWidth = Utils.Platform.TerminalWidth;
        var sb = new StringBuilder();

        if (_isSparse)
        {
            sb.Append('\n');
        }

        string line1Format = _env.Get("AURSH_PROMPT") ?? DefaultLine1Format;
        string line2Format = _env.Get("AURSH_PROMPT2") ?? DefaultLine2Format;

        string line1 = RenderLine(line1Format, workingDirectory, lastExitCode, termWidth, true);
        sb.Append(line1);

        if (_isVerbose)
        {
            string line2 = RenderLine(line2Format, workingDirectory, lastExitCode, termWidth, false);
            sb.Append('\n');
            sb.Append(line2);
        }
        else
        {
            sb.Append(' ');
        }

        return sb.ToString();
    }

    public int PromptVisibleLength(int lastExitCode)
    {
        ReloadConfig();
        if (_isVerbose)
        {
            string line2Format = _env.Get("AURSH_PROMPT2") ?? DefaultLine2Format;
            string line2 = RenderLine(line2Format, "", lastExitCode, 80, false);
            return Utils.Ansi.VisibleLength(line2);
        }
        else
        {
            // For 1-line prompt, the cursor is placed after the padded line 1, which means it wraps if it hits termWidth.
            // But we actually append a space at the end of line 1.
            // If the time segment is pushed to the right edge, the single space wraps the cursor to column 1 on the next line.
            // So visible length on the LAST line (where the cursor actually is) is 1.
            return 1;
        }
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

            int gap = termWidth - leftVisible - timeVisible - 1; // Prevent hitting the exact right edge to avoid double-wrap on \n
            if (gap > 0)
            {
                sb.Append(new string(_promptLineChar, gap));
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
            "battery" or "power" => BuildBatterySegment(),
            "network" => BuildNetworkSegment(),
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
            "version" => "3.0.0",
            "dollar" => System.Environment.UserName == "root" ? "#" : "$",
            "arrow" => "\u276F",
            "lambda" => "\u03BB",
            "branch" => _gitInfo.IsGitRepo ? _gitInfo.FormatStatus() : "",
            _ => ExpandCustomToken(token)
        };
    }

    private string ExpandCustomToken(string token)
    {
        if (_env.PluginManager != null)
        {
            string? pluginSegment = _env.PluginManager.EvaluatePromptSegment(token);
            if (pluginSegment != null)
            {
                return pluginSegment;
            }
        }
        return ExpandColorToken(token);
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
        sb.Append(_segmentEdge);

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
        sb.Append(_segmentEdge);

        sb.Append(Utils.Ansi.BgRgb(40, 40, 55));
        sb.Append(Utils.Ansi.FgRgb(120, 200, 255));
        string dirIcon = _gitInfo.IsGitRepo ? "\uE5FB" : "\uF115";
        sb.Append(" " + dirIcon + " ");
        sb.Append(dir);
        sb.Append(' ');
        sb.Append(Utils.Ansi.Reset);

        return sb.ToString();
    }

    private string BuildGitSegment()
    {
        if (!_gitInfo.IsGitRepo)
        {
            return "";
        }

        var sb = new StringBuilder();
        string gitFg = _gitInfo.IsDirty ? Utils.Ansi.FgRgb(255, 170, 100) : Utils.Ansi.FgRgb(130, 230, 150);
        string gitBg = Utils.Ansi.BgRgb(30, 30, 45);

        sb.Append(Utils.Ansi.FgRgb(40, 40, 55));
        sb.Append(gitBg);
        sb.Append(_segmentEdge);

        sb.Append(gitBg);
        sb.Append(gitFg);
        sb.Append(' ');

        if (!string.IsNullOrEmpty(_gitInfo.RemoteUrl))
        {
            sb.Append(Utils.Ansi.Hyperlink(_gitInfo.RemoteUrl, "\uEA84"));
            sb.Append(' ');
        }

        sb.Append(_gitInfo.FormatStatus());
        sb.Append(' ');
        sb.Append(Utils.Ansi.Reset);

        return sb.ToString();
    }

    private string BuildBatterySegment()
    {
        Utils.BatteryInfo.Refresh();
        if (!Utils.BatteryInfo.HasBattery)
        {
            return "";
        }

        var sb = new StringBuilder();
        string battBg = Utils.Ansi.BgRgb(45, 45, 55);
        string battFg;

        if (Utils.BatteryInfo.IsCharging || Utils.BatteryInfo.Percent > 60)
            battFg = Utils.Ansi.FgRgb(150, 255, 150);
        else if (Utils.BatteryInfo.Percent > 20)
            battFg = Utils.Ansi.FgRgb(255, 255, 100);
        else
            battFg = Utils.Ansi.FgRgb(255, 100, 100);

        string prevFg = _gitInfo.IsGitRepo ? Utils.Ansi.FgRgb(30, 30, 45) : Utils.Ansi.FgRgb(40, 40, 55);

        sb.Append(prevFg);
        sb.Append(battBg);
        sb.Append(_segmentEdge);

        sb.Append(battBg);
        sb.Append(battFg);
        sb.Append(" ");
        string icon;
        if (Utils.BatteryInfo.IsCharging) icon = "\udb80\udc84";
        else if (Utils.BatteryInfo.Percent >= 95) icon = "\uF240";
        else if (Utils.BatteryInfo.Percent >= 90) icon = "\udb80\udc82";
        else if (Utils.BatteryInfo.Percent >= 80) icon = "\udb80\udc81";
        else if (Utils.BatteryInfo.Percent >= 70) icon = "\udb80\udc80";
        else if (Utils.BatteryInfo.Percent >= 60) icon = "\udb80\udc7f";
        else if (Utils.BatteryInfo.Percent >= 50) icon = "\udb80\udc7e";
        else if (Utils.BatteryInfo.Percent >= 40) icon = "\udb80\udc7d";
        else if (Utils.BatteryInfo.Percent >= 30) icon = "\udb80\udc7c";
        else if (Utils.BatteryInfo.Percent >= 20) icon = "\udb80\udc7b";
        else if (Utils.BatteryInfo.Percent >= 10) icon = "\udb80\udc7a";
        else icon = "\udb80\udc83";

        sb.Append(icon);
        sb.Append(" ");
        sb.Append(Utils.BatteryInfo.Percent);
        sb.Append("% ");
        sb.Append(Utils.Ansi.Reset);

        return sb.ToString();
    }

    private string BuildNetworkSegment()
    {
        Utils.NetworkInfo.Refresh();

        var sb = new StringBuilder();
        string netBg = Utils.Ansi.BgRgb(20, 60, 40); // Dark green background to match aesthetic
        string netFg = (Utils.NetworkInfo.IsConnected && Utils.NetworkInfo.HasInternet) ? Utils.Ansi.FgRgb(150, 255, 150) : Utils.Ansi.FgRgb(255, 100, 100);

        string prevFg;
        if (Utils.BatteryInfo.HasBattery) prevFg = Utils.Ansi.FgRgb(45, 45, 55);
        else if (_gitInfo.IsGitRepo) prevFg = Utils.Ansi.FgRgb(30, 30, 45);
        else prevFg = Utils.Ansi.FgRgb(40, 40, 55);

        sb.Append(prevFg);
        sb.Append(netBg);
        sb.Append(_segmentEdge);

        sb.Append(netBg);
        sb.Append(netFg);
        sb.Append(" ");

        if (Utils.NetworkInfo.IsConnected)
        {
            if (!Utils.NetworkInfo.HasInternet)
            {
                // Connected locally, but no internet
                sb.Append("\udb82\udcfc ");
            }
            else if (Utils.NetworkInfo.IsWired)
            {
                sb.Append("\uDB80\uDE00 "); // nf-md-ethernet U+F0200
            }
            else
            {
                string icon = Utils.NetworkInfo.Bars switch
                {
                    4 => "\udb82\udcfa",
                    3 => "\udb82\udcf8",
                    2 => "\udb82\udcf6",
                    1 => "\udb82\udcf4",
                    _ => "\udb82\udcfc"
                };
                sb.Append(icon);
                sb.Append(" ");
            }
            sb.Append(Utils.NetworkInfo.Ssid);
        }
        else
        {
            sb.Append("\udb82\udcfc Disconnected");
        }
        sb.Append(" ");
        sb.Append(Utils.Ansi.Reset);

        sb.Append(Utils.Ansi.FgRgb(20, 60, 40));
        sb.Append(_segmentEdge);
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
