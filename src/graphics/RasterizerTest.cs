using System;

namespace AurShell.Graphics;

public class WindowElement : UIElement
{
    public string Title { get; set; }

    public override void Render(GraphicsContext g)
    {
        // Draw Window Background
        g.FillRectangle(X, Y, Width, Height, new Color32(255, 30, 30, 30));
        
        // Draw Border
        g.DrawRectangle(X, Y, Width, Height, Color32.White);
        
        // Draw Title Bar
        g.FillRectangle(X, Y, Width, 20, new Color32(255, 50, 50, 150));
        g.DrawText(Title ?? "Window", X + 5, Y + 6, Color32.White);
    }
}

public class LabelElement : UIElement
{
    public string Text { get; set; } = "";
    public Color32 TextColor { get; set; } = Color32.White;
    public bool WordWrap { get; set; } = false;

    public override void Render(GraphicsContext g)
    {
        if (!string.IsNullOrEmpty(Text))
        {
            if (WordWrap && Width > 0)
            {
                int maxCharsPerLine = Width / 8;
                if (maxCharsPerLine <= 0) maxCharsPerLine = 1;

                string[] words = Text.Split(' ');
                string currentLine = "";
                int currentY = Y;

                foreach (string word in words)
                {
                    if ((currentLine + word).Length > maxCharsPerLine)
                    {
                        g.DrawText(currentLine, X, currentY, TextColor);
                        currentY += 10;
                        currentLine = word + " ";
                    }
                    else
                    {
                        currentLine += word + " ";
                    }
                }
                if (currentLine.Length > 0)
                {
                    g.DrawText(currentLine, X, currentY, TextColor);
                }
            }
            else
            {
                g.DrawText(Text, X, Y, TextColor);
            }
        }
    }
}

public static class RasterizerTest
{
    public static void RunTest()
    {
        Console.WriteLine("Running Headless Rasterizer Test...");
        
        Compositor compositor = new Compositor(800, 600);
        compositor.BackgroundColor = new Color32(255, 10, 10, 10);

        WindowElement win1 = new WindowElement 
        {
            X = 50, Y = 50, Width = 400, Height = 300, 
            ZIndex = 1, Title = "Headless Window 1"
        };

        LabelElement lbl1 = new LabelElement
        {
            X = 70, Y = 100, ZIndex = 2, Text = "Hello from Software Rasterizer!", TextColor = Color32.Green
        };

        WindowElement win2 = new WindowElement 
        {
            X = 200, Y = 150, Width = 300, Height = 200, 
            ZIndex = 3, Title = "Overlapping Window"
        };

        LabelElement lbl2 = new LabelElement
        {
            X = 220, Y = 180, ZIndex = 4, Text = "This window is on top.", TextColor = Color32.Red
        };

        compositor.AddElement(win1);
        compositor.AddElement(lbl1);
        compositor.AddElement(win2);
        compositor.AddElement(lbl2);

        compositor.RenderPass();

        string outPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "test_render.bmp");
        ImageExporter.SaveToBmp(compositor.GetBuffer(), outPath);

        Console.WriteLine($"Test complete. Render saved to: {outPath}");
    }
}
