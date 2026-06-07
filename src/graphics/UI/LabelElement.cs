namespace AurShell.Graphics.UI;

using System;

public class LabelElement : UIElement
{
    public string Text { get; set; } = "";
    public Color32 TextColor { get; set; } = Color32.White;

    public int Scale { get; set; } = 1;
    public bool Bold { get; set; } = false;
    public bool Italic { get; set; } = false;
    public bool Strikethrough { get; set; } = false;
    public bool Underline { get; set; } = false;
    public Color32? BackgroundColor { get; set; } = null;

    public override void Render(GraphicsContext g)
    {
        // Debug bounding box
        // g.FillRectangle(X, Y, MeasureWidth(), MeasureHeight(), new Color32(100, 255, 0, 0));

        if (BackgroundColor.HasValue && !string.IsNullOrEmpty(Text))
        {
            g.FillRectangle(X, Y, MeasureWidth(), MeasureHeight(), BackgroundColor.Value);
        }

        if (!string.IsNullOrEmpty(Text))
        {
            g.DrawText(Text, X, Y, TextColor, Scale, Bold, Italic, Strikethrough, Underline);
        }
    }

    // Helper for layout
    public int MeasureWidth()
    {
        if (string.IsNullOrEmpty(Text)) return 0;
        int w = Text.Length * 8 * Scale;
        if (Bold) w += 1 * Text.Length; // bold adds 1px per char
        if (Italic) w += (Scale / 2) * Text.Length; // italic shift offset
        return w;
    }

    public int MeasureHeight()
    {
        return 8 * Scale;
    }
}
