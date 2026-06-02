# AurSh-View and Software Rasterizer

AurSh-View is a lightweight, built-in graphical user interface framework and software rasterizer for AurSh. It allows you to draw shapes, text, and windows directly to a virtual screen using the CPU, without needing complex GPU shaders.

This makes it incredibly easy to render custom UI elements or save graphics directly from scripts and shell commands!

---

## What is the Software Rasterizer?

A **Software Rasterizer** means that all the drawing (lines, rectangles, and text) is calculated by the computer's main processor (CPU) rather than the graphics card (GPU). 

It is divided into a few simple parts:

*   **`VirtualScreen`**: A canvas in memory. It holds a grid of pixels where everything is drawn.
*   **`Color32`**: Represents a color with Red, Green, Blue, and Alpha (transparency) values.
*   **`GraphicsContext`**: A set of drawing tools (like a paintbrush). It provides easy methods to draw shapes (`DrawLine`, `FillRectangle`) and text (`DrawText`) onto the `VirtualScreen`.

---

## How It Works Internally

AurSh-View processes graphics in a simple, step-by-step pipeline:

```mermaid
graph LR
    A[UI Elements\nWindows, Labels] -->|Added to| B(Compositor)
    B -->|Renders via| C(GraphicsContext)
    C -->|Paints Pixels on| D[(VirtualScreen)]
    D -->|Displayed by| E[SdlWindowHost\n(Actual Screen)]
```

1.  **UI Elements**: You create elements like `WindowElement` or `LabelElement` which know how to draw themselves.

2.  **Compositor**: This manages all the elements on the screen. It uses the **Painter's Algorithm**—meaning it sorts elements by their `ZIndex` and draws them from back to front, so items in the front cover items in the back.

3.  **VirtualScreen**: The Compositor commands the `GraphicsContext` to draw these elements onto the `VirtualScreen`.

4.  **SdlWindowHost**: Finally, the pixel data from the `VirtualScreen` is handed over to SDL (Simple DirectMedia Layer), which quickly uploads it to the actual computer monitor for you to see.

---

## Example: How to Use It

Here is a quick example of how you can create a simple scene with a window and text, and then draw it:

```csharp
using AurShell.Graphics;

// Create a Compositor (our scene manager) with an 800x600 resolution
Compositor compositor = new Compositor(800, 600);
compositor.BackgroundColor = new Color32(255, 10, 10, 10); // Dark gray

// Create a Window Element
WindowElement myWindow = new WindowElement 
{
    X = 50, Y = 50, 
    Width = 400, Height = 300, 
    ZIndex = 1, 
    Title = "My First Window"
};

// Create a Text Label Element
LabelElement myText = new LabelElement
{
    X = 70, Y = 100, 
    ZIndex = 2, 
    Text = "Hello World!", 
    TextColor = Color32.Green
};

// Add them to the Compositor
compositor.AddElement(myWindow);
compositor.AddElement(myText);

// Render everything to the VirtualScreen
compositor.RenderPass();

// You can now get the screen buffer to display it or save it as an image
VirtualScreen buffer = compositor.GetBuffer();
ImageExporter.SaveToBmp(buffer, "my_screenshot.bmp");
```

---

## Built-in Drawing Commands

If you want to draw your own custom graphics directly, you can use the `GraphicsContext`. Here are the main tools available:

*   **`Clear(Color32 color)`**: Fills the entire screen with a single color.
*   **`DrawLine(x0, y0, x1, y1, color)`**: Draws a straight line between two points.
*   **`DrawRectangle(x, y, width, height, color)`**: Draws the outline of a rectangle.
*   **`FillRectangle(x, y, width, height, color)`**: Draws a solid, filled rectangle.
*   **`DrawText(string text, x, y, color)`**: Draws text using a built-in blocky font.
*   **`Blit(VirtualScreen source, destX, destY)`**: Copies pixels from another screen onto this one, supporting transparency!
