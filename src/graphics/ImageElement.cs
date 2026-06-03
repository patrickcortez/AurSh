namespace AurShell.Graphics;

public class ImageElement : UIElement
{
    public VirtualScreen Image { get; set; }

    public override void Render(GraphicsContext g)
    {
        if (Image != null)
        {
            if (Width != Image.Width || Height != Image.Height)
            {
                g.BlitScaled(Image, X, Y, Width, Height);
            }
            else
            {
                g.Blit(Image, X, Y);
            }
        }
    }
}
