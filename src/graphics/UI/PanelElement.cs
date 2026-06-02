namespace AurShell.Graphics.UI;

using System.Collections.Generic;

public class PanelElement : UIElement
{
    public List<UIElement> Children { get; } = new List<UIElement>();
    public Color32 BackgroundColor { get; set; } = new Color32(0, 0, 0, 0);

    public override void Render(GraphicsContext g)
    {
        if (BackgroundColor.A > 0)
        {
            g.FillRectangle(X, Y, Width, Height, BackgroundColor);
        }

        foreach (var child in Children)
        {
            child.Render(g);
        }
    }

    public override void OnMouseMove(MouseEventArgs e)
    {
        foreach (var child in Children) child.OnMouseMove(e);
    }

    public override void OnMouseDown(MouseEventArgs e)
    {
        foreach (var child in Children) child.OnMouseDown(e);
    }

    public override void OnMouseUp(MouseEventArgs e)
    {
        foreach (var child in Children) child.OnMouseUp(e);
    }

    public override void OnMouseWheel(MouseEventArgs e)
    {
        foreach (var child in Children) child.OnMouseWheel(e);
    }
}
