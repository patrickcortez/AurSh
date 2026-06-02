namespace AurShell.Graphics.UI;

using System;
using System.Linq;
using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

public class MarkdownElement : PanelElement
{
    private string _markdownText = "";
    public string MarkdownText 
    { 
        get => _markdownText; 
        set 
        { 
            _markdownText = value; 
            BuildElements(); 
        } 
    }

    private void BuildElements()
    {
        Children.Clear();
        var pipeline = new MarkdownPipelineBuilder().UseAdvancedExtensions().Build();
        var doc = Markdown.Parse(_markdownText, pipeline);
        
        int currentY = 0;
        foreach (var block in doc)
        {
            currentY += ProcessBlock(block, 0, currentY, Width);
            currentY += 10;
        }
        Height = currentY;
    }

    private int ProcessBlock(Block block, int x, int y, int maxWidth)
    {
        if (block is HeadingBlock heading)
        {
            var text = GetInlineText(heading.Inline);
            Children.Add(new LabelElement { Text = text, TextColor = new Color32(255, 255, 215, 0), X = x, Y = y, ZIndex = 1, Width = maxWidth, WordWrap = true });
            int h = (text.Length / (Math.Max(1, maxWidth / 8)) + 1) * 10;
            return h + 10;
        }
        else if (block is ParagraphBlock paragraph)
        {
            var text = GetInlineText(paragraph.Inline);
            Children.Add(new LabelElement { Text = text, TextColor = Color32.White, X = x, Y = y, ZIndex = 1, Width = maxWidth, WordWrap = true });
            int h = (text.Length / (Math.Max(1, maxWidth / 8)) + 1) * 10;
            return h;
        }
        else if (block is QuoteBlock quote)
        {
            int qy = y;
            foreach (var qblock in quote)
            {
                qy += ProcessBlock(qblock, x + 15, qy, maxWidth - 15);
                qy += 5;
            }
            Children.Add(new PanelElement { X = x, Y = y, Width = 4, Height = qy - y, BackgroundColor = new Color32(255, 100, 100, 100) });
            return qy - y;
        }
        else if (block is ListBlock list)
        {
            int ly = y;
            int itemIdx = 1;
            foreach (var item in list)
            {
                if (item is ListItemBlock listItem)
                {
                    string bullet = list.IsOrdered ? $"{itemIdx}." : "*";
                    Children.Add(new LabelElement { Text = bullet, X = x, Y = ly, TextColor = new Color32(255, 0, 255, 255) });
                    int bx = x + 20;
                    foreach (var b in listItem)
                    {
                        ly += ProcessBlock(b, bx, ly, maxWidth - 20);
                        ly += 5;
                    }
                    itemIdx++;
                }
            }
            return ly - y;
        }
        else if (block is FencedCodeBlock code)
        {
            string codeText = string.Join("\n", code.Lines.Lines.Select(l => l.ToString()));
            int lines = code.Lines.Count;
            int ch = lines * 10 + 10;
            Children.Add(new PanelElement { X = x, Y = y, Width = maxWidth, Height = ch, BackgroundColor = new Color32(255, 30, 30, 30) });
            Children.Add(new LabelElement { Text = codeText, X = x + 5, Y = y + 5, TextColor = Color32.Green, ZIndex = 1 });
            return ch;
        }
        else if (block is CodeBlock inlineCodeBlock)
        {
            string codeText = string.Join("\n", inlineCodeBlock.Lines.Lines.Select(l => l.ToString()));
            int lines = inlineCodeBlock.Lines.Count;
            int ch = lines * 10 + 10;
            Children.Add(new PanelElement { X = x, Y = y, Width = maxWidth, Height = ch, BackgroundColor = new Color32(255, 30, 30, 30) });
            Children.Add(new LabelElement { Text = codeText, X = x + 5, Y = y + 5, TextColor = Color32.Green, ZIndex = 1 });
            return ch;
        }
        else if (block is ThematicBreakBlock)
        {
            Children.Add(new PanelElement { X = x, Y = y + 5, Width = maxWidth, Height = 2, BackgroundColor = new Color32(255, 100, 100, 100) });
            return 12;
        }
        else if (block is Markdig.Extensions.Tables.Table table)
        {
            int ty = y;
            foreach (var row in table)
            {
                if (row is Markdig.Extensions.Tables.TableRow tableRow)
                {
                    int tx = x;
                    int cellWidth = maxWidth / Math.Max(1, tableRow.Count);
                    int maxCellHeight = 0;
                    foreach (var cell in tableRow)
                    {
                        if (cell is Markdig.Extensions.Tables.TableCell tableCell)
                        {
                            int cy = ty;
                            foreach (var b in tableCell)
                            {
                                cy += ProcessBlock(b, tx + 5, cy + 5, cellWidth - 10);
                            }
                            if (cy - ty > maxCellHeight) maxCellHeight = cy - ty;
                        }
                        tx += cellWidth;
                    }
                    
                    Children.Add(new PanelElement { X = x, Y = ty + maxCellHeight + 5, Width = maxWidth, Height = 1, BackgroundColor = new Color32(255, 50, 50, 50) });
                    ty += maxCellHeight + 6;
                }
            }
            return ty - y;
        }
        
        return 0;
    }

    private string GetInlineText(ContainerInline? inlines)
    {
        if (inlines == null) return "";
        string res = "";
        foreach (var inline in inlines)
        {
            if (inline is LiteralInline literal)
            {
                res += literal.Content.ToString();
            }
            else if (inline is EmphasisInline emphasis)
            {
                res += GetInlineText(emphasis);
            }
            else if (inline is LinkInline link)
            {
                if (link.IsImage)
                {
                    res += $" [Image: {link.Url}] ";
                }
                else
                {
                    res += GetInlineText(link) + $" [{link.Url}]";
                }
            }
            else if (inline is CodeInline codeInline)
            {
                res += $"`{codeInline.Content}`";
            }
            else if (inline is ContainerInline container)
            {
                res += GetInlineText(container);
            }
        }
        return res;
    }
}
