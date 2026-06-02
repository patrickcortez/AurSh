namespace AurShell.Graphics;

public abstract class UIElement
{
    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public int ZIndex { get; set; }

    public abstract void Render(GraphicsContext g);
}
