namespace AurShell.Graphics.UI;

using System;

public class ScrollbarElement : UIElement
{
    public bool IsVertical { get; set; } = true;
    public int ContentSize { get; set; } = 1000;
    public int ViewportSize { get; set; } = 500;
    
    private int _scrollPosition = 0;
    public int ScrollPosition
    {
        get => _scrollPosition;
        set
        {
            int maxScroll = Math.Max(0, ContentSize - ViewportSize);
            _scrollPosition = Math.Clamp(value, 0, maxScroll);
        }
    }
    
    private bool _isDragging = false;
    private int _dragStartMouse = 0;
    private int _dragStartScroll = 0;

    public override void Render(GraphicsContext g)
    {
        g.FillRectangle(X, Y, Width, Height, new Color32(255, 40, 40, 40));
        
        if (ContentSize <= ViewportSize) return;

        float ratio = IsVertical ? (float)ViewportSize / ContentSize : (float)ViewportSize / ContentSize;
        int size = IsVertical ? Height : Width;
        int thumbSize = (int)(size * ratio);
        if (thumbSize < 10) thumbSize = 10;

        float scrollRatio = ContentSize > ViewportSize ? (float)ScrollPosition / (ContentSize - ViewportSize) : 0;
        
        Color32 thumbColor = _isDragging ? new Color32(255, 120, 120, 120) : new Color32(255, 80, 80, 80);

        if (IsVertical)
        {
            int thumbY = Y + (int)((Height - thumbSize) * scrollRatio);
            g.FillRoundedRectangle(X, thumbY, Width, thumbSize, 4, thumbColor);
        }
        else
        {
            int thumbX = X + (int)((Width - thumbSize) * scrollRatio);
            g.FillRoundedRectangle(thumbX, Y, thumbSize, Height, 4, thumbColor);
        }
    }

    public override void OnMouseDown(MouseEventArgs e)
    {
        bool hit = e.X >= X && e.X < X + Width && e.Y >= Y && e.Y < Y + Height;
        if (hit)
        {
            _isDragging = true;
            _dragStartMouse = IsVertical ? e.Y : e.X;
            _dragStartScroll = ScrollPosition;
            e.Handled = true;
        }
    }

    public override void OnMouseMove(MouseEventArgs e)
    {
        if (_isDragging)
        {
            int maxScroll = Math.Max(0, ContentSize - ViewportSize);
            if (maxScroll > 0)
            {
                int size = IsVertical ? Height : Width;
                float ratio = (float)ViewportSize / ContentSize;
                int thumbSize = Math.Max(10, (int)(size * ratio));
                int trackSize = size - thumbSize;
                
                int mouseDelta = (IsVertical ? e.Y : e.X) - _dragStartMouse;
                float scrollDelta = trackSize > 0 ? ((float)mouseDelta / trackSize) * maxScroll : 0;
                
                ScrollPosition = _dragStartScroll + (int)scrollDelta;
                e.Handled = true;
            }
        }
    }

    public override void OnMouseUp(MouseEventArgs e)
    {
        _isDragging = false;
    }
}
