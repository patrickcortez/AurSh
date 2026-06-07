namespace AurShell.Graphics;

public class MouseEventArgs
{
    public int X { get; set; }
    public int Y { get; set; }
    public int DeltaY { get; set; }
    public int Button { get; set; }
    public bool Handled { get; set; }
}
