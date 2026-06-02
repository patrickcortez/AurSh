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
}
