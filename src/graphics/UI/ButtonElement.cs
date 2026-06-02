namespace AurShell.Graphics.UI;

using System;

public class ButtonElement : UIElement
{
    public string Text { get; set; } = "";
    public Action? OnClick { get; set; }
    
    private bool _isHovered;
    private bool _isPressed;

    public override void Render(GraphicsContext g)
    {
        Color32 bgColor = _isPressed ? new Color32(255, 100, 100, 100) : 
                          _isHovered ? new Color32(255, 150, 150, 150) : 
                                       new Color32(255, 60, 60, 60);

        g.FillRoundedRectangle(X, Y, Width, Height, 5, bgColor);
        
        int textWidth = Text.Length * 8;
        int tx = X + (Width - textWidth) / 2;
        int ty = Y + (Height - 8) / 2;
        g.DrawText(Text, tx, ty, Color32.White);
    }

    public override void OnMouseMove(MouseEventArgs e)
    {
        bool hit = e.X >= X && e.X < X + Width && e.Y >= Y && e.Y < Y + Height;
        _isHovered = hit;
    }

    public override void OnMouseDown(MouseEventArgs e)
    {
        bool hit = e.X >= X && e.X < X + Width && e.Y >= Y && e.Y < Y + Height;
        if (hit) _isPressed = true;
    }

    public override void OnMouseUp(MouseEventArgs e)
    {
        bool hit = e.X >= X && e.X < X + Width && e.Y >= Y && e.Y < Y + Height;
        if (_isPressed && hit)
        {
            OnClick?.Invoke();
            e.Handled = true;
        }
        _isPressed = false;
    }
}
