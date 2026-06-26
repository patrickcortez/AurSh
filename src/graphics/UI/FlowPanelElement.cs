namespace AurShell.Graphics.UI;

using System.Collections.Generic;
using System.Linq;

public class FlowPanelElement : UIElement
{
    public List<UIElement> Children { get; } = new List<UIElement>();

    // Spacing between elements
    public int HorizontalSpacing { get; set; } = 4;
    public int VerticalSpacing { get; set; } = 4;
    public string Align { get; set; } = "left";

    public override void Render(GraphicsContext g)
    {
        var rows = new List<List<UIElement>>();
        var currentRow = new List<UIElement>();
        var rowWidths = new List<int>();

        int curX = 0;

        foreach (var child in Children)
        {
            int childWidth = child.Width;
            if (child is LabelElement lbl) childWidth = lbl.MeasureWidth();

            if (curX + childWidth > Width && curX > 0)
            {
                rows.Add(currentRow);
                rowWidths.Add(curX - HorizontalSpacing);
                currentRow = new List<UIElement>();
                curX = 0;
            }

            currentRow.Add(child);
            curX += childWidth + HorizontalSpacing;
        }
        if (currentRow.Count > 0)
        {
            rows.Add(currentRow);
            rowWidths.Add(curX - HorizontalSpacing);
        }

        int curY = 0;
        for (int i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            int rWidth = rowWidths[i];

            int offsetX = 0;
            if (Align == "center" && rWidth < Width) offsetX = (Width - rWidth) / 2;
            else if (Align == "right" && rWidth < Width) offsetX = Width - rWidth;

            curX = offsetX;
            int rHeight = 0;

            foreach (var child in row)
            {
                int childWidth = child.Width;
                int childHeight = child.Height;
                if (child is LabelElement lbl)
                {
                    childWidth = lbl.MeasureWidth();
                    childHeight = lbl.MeasureHeight();
                }

                child.X = this.X + curX;
                child.Y = this.Y + curY;
                child.Render(g);

                curX += childWidth + HorizontalSpacing;
                if (childHeight > rHeight) rHeight = childHeight;
            }

            curY += rHeight + VerticalSpacing;
        }
    }

    // Calculate total height required
    public int MeasureTotalHeight()
    {
        int curX = 0;
        int curY = 0;
        int rowHeight = 0;

        foreach (var child in Children)
        {
            int childWidth = child.Width;
            int childHeight = child.Height;

            if (child is LabelElement lbl)
            {
                childWidth = lbl.MeasureWidth();
                childHeight = lbl.MeasureHeight();
            }

            if (curX + childWidth > Width && curX > 0)
            {
                curX = 0;
                curY += rowHeight + VerticalSpacing;
                rowHeight = 0;
            }

            curX += childWidth + HorizontalSpacing;
            if (childHeight > rowHeight)
            {
                rowHeight = childHeight;
            }
        }

        return curY + rowHeight;
    }
}
