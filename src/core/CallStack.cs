namespace AurShell.Core;

public enum FrameType
{
    Function,
    Arithmetic,
    Subshell,
    Source
}

public class StackFrame
{
    public string Name { get; }
    public int Line { get; }
    public int Column { get; }
    public string File { get; }
    public FrameType Type { get; }

    public StackFrame(string name, int line, int column, string file, FrameType type)
    {
        Name = name;
        Line = line;
        Column = column;
        File = file;
        Type = type;
    }

    public override string ToString()
    {
        string typeStr = Type switch
        {
            FrameType.Function => "function",
            FrameType.Arithmetic => "math evaluation",
            FrameType.Subshell => "subshell",
            FrameType.Source => "source",
            _ => "unknown"
        };
        return $"at {typeStr} '{Name}' (line {Line}, col {Column})";
    }
}
