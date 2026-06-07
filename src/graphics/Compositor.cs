using System.Collections.Generic;
using System.Linq;

namespace AurShell.Graphics;

public class Compositor
{
    private VirtualScreen _screen;
    private GraphicsContext _graphics;
    private List<UIElement> _elements;

    public Color32 BackgroundColor { get; set; } = Color32.Black;

    public Compositor(int width, int height)
    {
        _screen = new VirtualScreen(width, height);
        _graphics = new GraphicsContext(_screen);
        _elements = new List<UIElement>();
    }

    public void AddElement(UIElement element)
    {
        _elements.Add(element);
    }

    public void RemoveElement(UIElement element)
    {
        _elements.Remove(element);
    }

    public void RenderPass()
    {
        _graphics.Clear(BackgroundColor);

        // Painter's Algorithm
        var sortedElements = _elements.OrderBy(e => e.ZIndex).ToList();
        foreach (var element in sortedElements)
        {
            element.Render(_graphics);
        }
    }

    public VirtualScreen GetBuffer()
    {
        return _screen;
    }

    public void HandleMouseEvent(MouseEventArgs e, string eventType)
    {
        // Route from front to back
        var sortedElements = _elements.OrderByDescending(el => el.ZIndex).ToList();
        foreach (var element in sortedElements)
        {
            if (e.Handled) break;

            bool hit = e.X >= element.X && e.X < element.X + element.Width &&
                       e.Y >= element.Y && e.Y < element.Y + element.Height;

            // Send events to element. Hover/Motion sometimes doesn't require a hit if they capture, but keep it simple.
            // Wheel events might not require strict hit if focus is held, but we'll do hit testing.
            if (hit || eventType == "Wheel" || eventType == "MouseUp" || eventType == "MouseMove")
            {
                // Simple routing: if it's within bounds, give it a chance
                if (eventType == "MouseMove") element.OnMouseMove(e);
                else if (eventType == "MouseDown" && hit) element.OnMouseDown(e);
                else if (eventType == "MouseUp") element.OnMouseUp(e);
                else if (eventType == "Wheel" && hit) element.OnMouseWheel(e);
            }
        }
    }
}
