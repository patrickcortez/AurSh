namespace AurShell.Graphics.UI;

using System;
using System.Collections.Generic;
using System.Linq;

public class MathElement : UIElement
{
    public string MathText { get; set; } = "";
    public Color32 TextColor { get; set; } = new Color32(255, 0, 255, 255);
    public bool IsBlock { get; set; } = false;

    public int MeasureHeight()
    {
        return IsBlock ? 40 : 10;
    }

    public override void Render(GraphicsContext g)
    {
        if (string.IsNullOrEmpty(MathText)) return;

        if (IsBlock)
        {
            g.FillRectangle(X, Y, Width, MeasureHeight(), new Color32(255, 30, 40, 50));
        }

        int startY = IsBlock ? Y + 15 : Y;
        int startX = IsBlock ? X + Width / 2 - (MathText.Length * 8) / 2 : X;

        // Simple rendering for math: italicized, with cyan color
        // A true LaTeX renderer would parse superscripts, fractions etc.
        // For now we rely on the italic graphics context engine.
        g.DrawText(MathText, startX, startY, TextColor, 1, false, true);
    }
}
