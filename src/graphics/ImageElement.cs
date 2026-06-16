namespace AurShell.Graphics;

public class ImageElement : UIElement
{
    private VirtualScreen _image;
    public VirtualScreen Image 
    { 
        get => _image; 
        set 
        { 
            _image = value; 
            if (Width <= 0 && value != null) Width = value.Width;
            if (Height <= 0 && value != null) Height = value.Height;
        } 
    }

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
