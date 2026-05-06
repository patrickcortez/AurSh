using System.Text;

namespace AurShell.Core;

public class Prompt
{
    private readonly ShellEnvironment _env;
    private readonly Utils.GitInfo _gitInfo = new();

    private const string BoxTopLeft = "\u256D\u2500";
    private const string BoxBottomLeft = "\u2570\u2500";
    private const string Separator = " \u2500 ";
    private const string PowerlineSep = "\uE0B0";
    private const string GitBranch = "\uE0A0";

    public Prompt(ShellEnvironment env)
    {
        _env = env;
    }

    public string Render(string workingDirectory, int lastExitCode)
    {
        _gitInfo.Refresh(workingDirectory);

        int termWidth = Utils.Platform.TerminalWidth;
        var sb = new StringBuilder();

        string line1 = BuildLine1(workingDirectory, lastExitCode, termWidth);
        string line2 = BuildLine2(lastExitCode);

        sb.Append(line1);
        sb.Append('\n');
        sb.Append(line2);

        return sb.ToString();
    }

    public int PromptVisibleLength(int lastExitCode)
    {
        string line2 = BuildLine2(lastExitCode);
        return Utils.Ansi.VisibleLength(line2);
    }

    private string BuildLine1(string workingDirectory, int lastExitCode, int termWidth)
    {
        var sb = new StringBuilder();

        string osIcon = Utils.Platform.OsIcon;
        string osColor = GetOsColor();
        string osBg = GetOsBgColor();

        string user = Utils.Platform.UserName;
        string host = Utils.Platform.HostName;
        string dir = Utils.Platform.ShortenPath(workingDirectory);
        string time = DateTime.Now.ToString("HH:mm:ss");

        sb.Append(Utils.Ansi.FgBrightBlack);
        sb.Append(BoxTopLeft);
        sb.Append(' ');

        sb.Append(osBg);
        sb.Append(Utils.Ansi.FgWhite);
        sb.Append(Utils.Ansi.Bold);
        sb.Append(' ');
        sb.Append(osIcon);
        sb.Append(' ');
        sb.Append(Utils.Ansi.Reset);

        sb.Append(Utils.Ansi.FgFromBg(osBg));
        sb.Append(Utils.Ansi.BgRgb(50, 50, 70));
        sb.Append(PowerlineSep);

        sb.Append(Utils.Ansi.BgRgb(50, 50, 70));
        sb.Append(Utils.Ansi.FgRgb(180, 210, 255));
        sb.Append(' ');
        sb.Append(user);
        sb.Append(Utils.Ansi.FgRgb(100, 100, 130));
        sb.Append('@');
        sb.Append(Utils.Ansi.FgRgb(140, 170, 220));
        sb.Append(host);
        sb.Append(' ');
        sb.Append(Utils.Ansi.Reset);

        sb.Append(Utils.Ansi.FgRgb(50, 50, 70));
        sb.Append(Utils.Ansi.BgRgb(40, 40, 55));
        sb.Append(PowerlineSep);

        sb.Append(Utils.Ansi.BgRgb(40, 40, 55));
        sb.Append(Utils.Ansi.FgRgb(120, 200, 255));
        sb.Append(" \uF115 ");
        sb.Append(dir);
        sb.Append(' ');
        sb.Append(Utils.Ansi.Reset);

        if (_gitInfo.IsGitRepo)
        {
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
        }
        else
        {
            sb.Append(Utils.Ansi.FgRgb(40, 40, 55));
            sb.Append(PowerlineSep);
            sb.Append(Utils.Ansi.Reset);
        }

        if (lastExitCode != 0)
        {
            sb.Append(Utils.Ansi.FgRgb(255, 100, 100));
            sb.Append(" \u2718 ");
            sb.Append(lastExitCode);
            sb.Append(Utils.Ansi.Reset);
        }

        string leftPart = sb.ToString();
        int leftVisible = Utils.Ansi.VisibleLength(leftPart);

        string timeSegment = BuildTimeSegment(time);
        int timeVisible = Utils.Ansi.VisibleLength(timeSegment);

        int gap = termWidth - leftVisible - timeVisible;
        if (gap > 0)
        {
            sb.Append(new string(' ', gap));
            sb.Append(timeSegment);
        }

        return sb.ToString();
    }

    private string BuildLine2(int lastExitCode)
    {
        var sb = new StringBuilder();

        sb.Append(Utils.Ansi.FgBrightBlack);
        sb.Append(BoxBottomLeft);
        sb.Append(' ');

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
        sb.Append(' ');

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

    private string GetOsColor()
    {
        return Utils.Platform.CurrentOS switch
        {
            Utils.OperatingSystemType.Windows => Utils.Ansi.FgRgb(0, 120, 215),
            Utils.OperatingSystemType.MacOS => Utils.Ansi.FgRgb(160, 160, 160),
            Utils.OperatingSystemType.Linux => Utils.Ansi.FgRgb(230, 170, 50),
            Utils.OperatingSystemType.Termux => Utils.Ansi.FgRgb(60, 180, 75),
            _ => Utils.Ansi.FgWhite
        };
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
