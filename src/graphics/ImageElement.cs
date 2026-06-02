namespace AurShell.Graphics;

public class ImageElement : UIElement
{
    public VirtualScreen Image { get; set; }

    public override void Render(GraphicsContext g)
    {
        if (Image != null)
        {
            g.Blit(Image, X, Y);
        }
    }
}
