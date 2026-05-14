using System.Text;

namespace AurShell.BlackBoxView;

public sealed class AnsiStateTracker
{
    private int _fgMode;
    private int _fgBasic;
    private int _fg256;
    private int _fgR, _fgG, _fgB;

    private int _bgMode;
    private int _bgBasic;
    private int _bg256;
    private int _bgR, _bgG, _bgB;

    private bool _bold;
    private bool _dim;
    private bool _italic;
    private bool _underline;
    private bool _blink;
    private bool _reverse;
    private bool _hidden;
    private bool _strikethrough;

    public bool HasState =>
        _fgMode != 0 || _bgMode != 0 ||
        _bold || _dim || _italic || _underline ||
        _blink || _reverse || _hidden || _strikethrough;

    public void Reset()
    {
        _fgMode = 0;
        _fgBasic = 0;
        _fg256 = 0;
        _fgR = _fgG = _fgB = 0;

        _bgMode = 0;
        _bgBasic = 0;
        _bg256 = 0;
        _bgR = _bgG = _bgB = 0;

        _bold = false;
        _dim = false;
        _italic = false;
        _underline = false;
        _blink = false;
        _reverse = false;
        _hidden = false;
        _strikethrough = false;
    }

    public void ProcessLine(string line)
    {
        int i = 0;
        while (i < line.Length)
        {
            if (line[i] == '\x1b' && i + 1 < line.Length && line[i + 1] == '[')
            {
                i += 2;
                int seqStart = i;

                while (i < line.Length && ((line[i] >= '0' && line[i] <= '9') || line[i] == ';'))
                    i++;

                if (i < line.Length && line[i] == 'm')
                {
                    string paramStr = line.Substring(seqStart, i - seqStart);
                    ApplySgrParams(paramStr);
                    i++;
                }
                else
                {
                    if (i < line.Length)
                        i++;
                }
            }
            else
            {
                i++;
            }
        }
    }

    private void ApplySgrParams(string paramStr)
    {
        if (string.IsNullOrEmpty(paramStr))
        {
            Reset();
            return;
        }

        int[] codes = ParseSgrCodes(paramStr);
        int idx = 0;

        while (idx < codes.Length)
        {
            int code = codes[idx];

            switch (code)
            {
                case 0:
                    Reset();
                    break;
                case 1:
                    _bold = true;
                    break;
                case 2:
                    _dim = true;
                    break;
                case 3:
                    _italic = true;
                    break;
                case 4:
                    _underline = true;
                    break;
                case 5:
                    _blink = true;
                    break;
                case 7:
                    _reverse = true;
                    break;
                case 8:
                    _hidden = true;
                    break;
                case 9:
                    _strikethrough = true;
                    break;

                case 22:
                    _bold = false;
                    _dim = false;
                    break;
                case 23:
                    _italic = false;
                    break;
                case 24:
                    _underline = false;
                    break;
                case 25:
                    _blink = false;
                    break;
                case 27:
                    _reverse = false;
                    break;
                case 28:
                    _hidden = false;
                    break;
                case 29:
                    _strikethrough = false;
                    break;

                case >= 30 and <= 37:
                    _fgMode = 1;
                    _fgBasic = code;
                    break;
                case 38:
                    idx = ParseExtendedColor(codes, idx, out _fgMode, out _fg256, out _fgR, out _fgG, out _fgB);
                    break;
                case 39:
                    _fgMode = 0;
                    break;

                case >= 40 and <= 47:
                    _bgMode = 1;
                    _bgBasic = code;
                    break;
                case 48:
                    idx = ParseExtendedColor(codes, idx, out _bgMode, out _bg256, out _bgR, out _bgG, out _bgB);
                    break;
                case 49:
                    _bgMode = 0;
                    break;

                case >= 90 and <= 97:
                    _fgMode = 1;
                    _fgBasic = code;
                    break;
                case >= 100 and <= 107:
                    _bgMode = 1;
                    _bgBasic = code;
                    break;
            }

            idx++;
        }
    }

    private static int ParseExtendedColor(int[] codes, int idx, out int mode, out int c256, out int r, out int g, out int b)
    {
        c256 = 0;
        r = g = b = 0;

        if (idx + 1 < codes.Length && codes[idx + 1] == 5)
        {
            if (idx + 2 < codes.Length)
            {
                mode = 2;
                c256 = codes[idx + 2];
                return idx + 2;
            }
            mode = 0;
            return idx + 1;
        }

        if (idx + 1 < codes.Length && codes[idx + 1] == 2)
        {
            if (idx + 4 < codes.Length)
            {
                mode = 3;
                r = codes[idx + 2];
                g = codes[idx + 3];
                b = codes[idx + 4];
                return idx + 4;
            }
            mode = 0;
            return idx + 1;
        }

        mode = 0;
        return idx;
    }

    private static int[] ParseSgrCodes(string paramStr)
    {
        var parts = paramStr.Split(';');
        var result = new int[parts.Length];

        for (int i = 0; i < parts.Length; i++)
        {
            if (int.TryParse(parts[i], out int val))
                result[i] = val;
            else
                result[i] = 0;
        }

        return result;
    }

    public string GetStatePrefix()
    {
        if (!HasState)
            return "";

        var sb = new StringBuilder();

        if (_bold) sb.Append("\x1b[1m");
        if (_dim) sb.Append("\x1b[2m");
        if (_italic) sb.Append("\x1b[3m");
        if (_underline) sb.Append("\x1b[4m");
        if (_blink) sb.Append("\x1b[5m");
        if (_reverse) sb.Append("\x1b[7m");
        if (_hidden) sb.Append("\x1b[8m");
        if (_strikethrough) sb.Append("\x1b[9m");

        switch (_fgMode)
        {
            case 1:
                sb.Append($"\x1b[{_fgBasic}m");
                break;
            case 2:
                sb.Append($"\x1b[38;5;{_fg256}m");
                break;
            case 3:
                sb.Append($"\x1b[38;2;{_fgR};{_fgG};{_fgB}m");
                break;
        }

        switch (_bgMode)
        {
            case 1:
                sb.Append($"\x1b[{_bgBasic}m");
                break;
            case 2:
                sb.Append($"\x1b[48;5;{_bg256}m");
                break;
            case 3:
                sb.Append($"\x1b[48;2;{_bgR};{_bgG};{_bgB}m");
                break;
        }

        return sb.ToString();
    }
}
