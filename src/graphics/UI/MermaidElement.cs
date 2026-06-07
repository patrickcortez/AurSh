namespace AurShell.Graphics.UI;

using System;
using System.Collections.Generic;
using System.Linq;

public class MermaidElement : UIElement
{
    public string MermaidText { get; set; } = "";
    public Color32 TextColor { get; set; } = Color32.White;
    public Color32 NodeColor { get; set; } = new Color32(255, 50, 150, 255);
    public Color32 LineColor { get; set; } = new Color32(255, 200, 200, 200);

    public int MeasureHeight()
    {
        return 200; // Fixed height for simple diagrams for now
    }

    public override void Render(GraphicsContext g)
    {
        if (string.IsNullOrEmpty(MermaidText)) return;

        // Extremely basic parser for "A-->B" or "A --> B"
        var lines = MermaidText.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);

        var nodes = new HashSet<string>();
        var edges = new List<Tuple<string, string>>();

        foreach (var line in lines)
        {
            var l = line.Trim();
            if (l.StartsWith("graph") || l.StartsWith("flowchart")) continue;

            var parts = l.Split(new[] { "-->", "---", "-.->", "==>" }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 2)
            {
                string from = parts[0].Trim();
                string to = parts[1].Trim();
                nodes.Add(from);
                nodes.Add(to);
                edges.Add(Tuple.Create(from, to));
            }
        }

        if (nodes.Count == 0)
        {
            g.DrawText("Invalid or unsupported Mermaid graph", X, Y, new Color32(255, 255, 0, 0));
            return;
        }

        // Layout: Just arrange nodes in a circle
        int cx = X + Width / 2;
        int cy = Y + MeasureHeight() / 2;
        int r = Math.Min(Width, MeasureHeight()) / 2 - 30;

        var nodeList = nodes.ToList();
        var nodePositions = new Dictionary<string, (int x, int y)>();

        for (int i = 0; i < nodeList.Count; i++)
        {
            double angle = i * 2 * Math.PI / nodeList.Count;
            int nx = cx + (int)(r * Math.Cos(angle));
            int ny = cy + (int)(r * Math.Sin(angle));
            nodePositions[nodeList[i]] = (nx, ny);
        }

        // Draw edges
        foreach (var edge in edges)
        {
            var p1 = nodePositions[edge.Item1];
            var p2 = nodePositions[edge.Item2];
            g.DrawLine(p1.x, p1.y, p2.x, p2.y, LineColor);
        }

        // Draw nodes
        foreach (var kvp in nodePositions)
        {
            g.DrawCircle(kvp.Value.x, kvp.Value.y, 20, NodeColor);

            // Draw node text centered
            int textW = kvp.Key.Length * 8;
            g.DrawText(kvp.Key, kvp.Value.x - textW / 2, kvp.Value.y - 4, TextColor);
        }
    }
}
