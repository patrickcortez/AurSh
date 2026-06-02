namespace AurShell.Graphics.UI;

using System;

public class ScrollViewerElement : UIElement
{
    public PanelElement Content { get; set; } = new PanelElement();
    public ScrollbarElement VerticalScrollbar { get; set; } = new ScrollbarElement { IsVertical = true, Width = 15 };
    public ScrollbarElement HorizontalScrollbar { get; set; } = new ScrollbarElement { IsVertical = false, Height = 15 };

    private VirtualScreen? _contentBuffer;

    public void MeasureAndLayout()
    {
        // Recursively find max X and Y of children to determine ContentSize
        int maxW = Width;
        int maxH = Height;
        foreach (var child in Content.Children)
        {
            if (child.X + child.Width > maxW) maxW = child.X + child.Width;
            if (child.Y + child.Height > maxH) maxH = child.Y + child.Height;
        }

        VerticalScrollbar.ContentSize = maxH;
        VerticalScrollbar.ViewportSize = Height - HorizontalScrollbar.Height;
        
        HorizontalScrollbar.ContentSize = maxW;
        HorizontalScrollbar.ViewportSize = Width - VerticalScrollbar.Width;

        if (_contentBuffer == null || _contentBuffer.Width < maxW || _contentBuffer.Height < maxH)
        {
            _contentBuffer = new VirtualScreen(Math.Max(maxW, 800), Math.Max(maxH, 600));
        }
    }

    public override void Render(GraphicsContext g)
    {
        MeasureAndLayout();

        // 1. Render all children into the internal buffer
        _contentBuffer!.Clear(new Color32(0, 0, 0, 0));
        var contentGraphics = new GraphicsContext(_contentBuffer);
        Content.Render(contentGraphics);

        // 2. Blit the visible region from the internal buffer to the main screen
        int viewW = Width - VerticalScrollbar.Width;
        int viewH = Height - HorizontalScrollbar.Height;
        
        g.BlitRegion(_contentBuffer, 
                     HorizontalScrollbar.ScrollPosition, 
                     VerticalScrollbar.ScrollPosition, 
                     viewW, viewH, 
                     X, Y);

        // 3. Render Scrollbars
        VerticalScrollbar.X = X + viewW;
        VerticalScrollbar.Y = Y;
        VerticalScrollbar.Height = viewH;
        VerticalScrollbar.Render(g);

        HorizontalScrollbar.X = X;
        HorizontalScrollbar.Y = Y + viewH;
        HorizontalScrollbar.Width = viewW;
        HorizontalScrollbar.Render(g);
        
        // Render corner block
        g.FillRectangle(X + viewW, Y + viewH, VerticalScrollbar.Width, HorizontalScrollbar.Height, new Color32(255, 30, 30, 30));
    }

    public override void OnMouseDown(MouseEventArgs e)
    {
        VerticalScrollbar.OnMouseDown(e);
        HorizontalScrollbar.OnMouseDown(e);
        if (!e.Handled)
        {
            // Translate coordinates for content
            int viewW = Width - VerticalScrollbar.Width;
            int viewH = Height - HorizontalScrollbar.Height;
            if (e.X >= X && e.X < X + viewW && e.Y >= Y && e.Y < Y + viewH)
            {
                var ce = new MouseEventArgs { X = e.X - X + HorizontalScrollbar.ScrollPosition, Y = e.Y - Y + VerticalScrollbar.ScrollPosition, Button = e.Button };
                Content.OnMouseDown(ce);
                if (ce.Handled) e.Handled = true;
            }
        }
    }

    public override void OnMouseMove(MouseEventArgs e)
    {
        VerticalScrollbar.OnMouseMove(e);
        HorizontalScrollbar.OnMouseMove(e);
        
        int viewW = Width - VerticalScrollbar.Width;
        int viewH = Height - HorizontalScrollbar.Height;
        if (e.X >= X && e.X < X + viewW && e.Y >= Y && e.Y < Y + viewH)
        {
            var ce = new MouseEventArgs { X = e.X - X + HorizontalScrollbar.ScrollPosition, Y = e.Y - Y + VerticalScrollbar.ScrollPosition };
            Content.OnMouseMove(ce);
        }
    }

    public override void OnMouseUp(MouseEventArgs e)
    {
        VerticalScrollbar.OnMouseUp(e);
        HorizontalScrollbar.OnMouseUp(e);
        
        int viewW = Width - VerticalScrollbar.Width;
        int viewH = Height - HorizontalScrollbar.Height;
        if (e.X >= X && e.X < X + viewW && e.Y >= Y && e.Y < Y + viewH)
        {
            var ce = new MouseEventArgs { X = e.X - X + HorizontalScrollbar.ScrollPosition, Y = e.Y - Y + VerticalScrollbar.ScrollPosition, Button = e.Button };
            Content.OnMouseUp(ce);
        }
    }

    public override void OnMouseWheel(MouseEventArgs e)
    {
        // Scroll vertically by default
        VerticalScrollbar.ScrollPosition -= e.DeltaY * 40;
        e.Handled = true;
    }
}
