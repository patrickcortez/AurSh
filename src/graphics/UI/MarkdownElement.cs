namespace AurShell.Graphics.UI;

using System;
using System.Linq;
using System.Collections.Generic;
using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using Markdig.Extensions.Mathematics;
using System.Net.Http;
using System.IO;

public class MarkdownElement : PanelElement
{
    public string BasePath { get; set; } = Environment.CurrentDirectory; // issue # 1: This is gettig the path of AurSh's Exe, not the parent folder path of the md file.
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
            int h = ProcessBlock(block, 0, currentY, Width, 0);
            if (h > 0)
            {
                currentY += h;
                currentY += 10;
            }
        }
        Height = currentY;
    }

    private int ProcessBlock(Block block, int x, int y, int maxWidth, int nestingLevel = 0)
    {
        if (block is HeadingBlock heading)
        {
            int scale = 1;
            bool bold = true;
            if (heading.Level == 1) scale = 3;
            else if (heading.Level == 2) scale = 2;

            var flow = new FlowPanelElement { X = x, Y = y, Width = maxWidth, HorizontalSpacing = 0, VerticalSpacing = 2 };
            ProcessInlines(heading.Inline, flow, scale, bold, new Color32(255, 255, 215, 0));
            
            int flowHeight = flow.MeasureTotalHeight();
            flow.Height = flowHeight;
            Children.Add(flow);
            
            // Draw underline for h1 and h2
            if (heading.Level <= 2)
            {
                Children.Add(new PanelElement { X = x, Y = y + flowHeight + 4, Width = maxWidth, Height = 2, BackgroundColor = new Color32(255, 100, 100, 100) });
                return flowHeight + 10;
            }
            return flowHeight + 5;
        }
        else if (block is ParagraphBlock paragraph)
        {
            var flow = new FlowPanelElement { X = x, Y = y, Width = maxWidth, HorizontalSpacing = 0, VerticalSpacing = 4 };
            ProcessInlines(paragraph.Inline, flow, 1, false, Color32.White);
            
            int flowHeight = flow.MeasureTotalHeight();
            flow.Height = flowHeight;
            Children.Add(flow);
            return flowHeight;
        }
        else if (block is QuoteBlock quote)
        {
            int qy = y;
            Children.Add(new PanelElement { X = x, Y = y, Width = 2, Height = 10, BackgroundColor = new Color32(255, 150, 150, 150) });
            
            foreach (var b in quote)
            {
                qy += ProcessBlock(b, x + 15, qy, maxWidth - 15, nestingLevel);
            }
            
            var panel = Children.Last(c => c is PanelElement && c.Width == 2) as PanelElement;
            if (panel != null) panel.Height = qy - y;

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
                    string bullet = list.IsOrdered ? $"{itemIdx}. " : (nestingLevel % 3 == 0 ? "* " : (nestingLevel % 3 == 1 ? "o " : "- "));
                    
                    // Task list check
                    bool isTask = false;
                    bool isChecked = false;
                    if (listItem.Count > 0 && listItem[0] is ParagraphBlock pb && pb.Inline != null)
                    {
                        var firstInline = pb.Inline.FirstOrDefault();
                        if (firstInline is LiteralInline literal)
                        {
                            string text = literal.Content.ToString();
                            if (text.StartsWith("[ ] ")) { isTask = true; isChecked = false; literal.Content = new Markdig.Helpers.StringSlice(text.Substring(4)); }
                            else if (text.StartsWith("[x] ") || text.StartsWith("[X] ")) { isTask = true; isChecked = true; literal.Content = new Markdig.Helpers.StringSlice(text.Substring(4)); }
                        }
                    }

                    UIElement bulletElement;
                    if (isTask)
                    {
                        bulletElement = new LabelElement { Text = isChecked ? "[X]" : "[ ]", X = x, Y = ly, TextColor = isChecked ? new Color32(255, 0, 255, 0) : new Color32(255, 255, 0, 0) };
                    }
                    else
                    {
                        bulletElement = new LabelElement { Text = bullet, X = x, Y = ly, TextColor = new Color32(255, 0, 255, 255) };
                    }
                    
                    Children.Add(bulletElement);
                    
                    int bWidthMeasure = (bulletElement as LabelElement)?.MeasureWidth() ?? 24;
                    int bx = x + bWidthMeasure + (isTask ? 5 : 0);
                    int bWidth = maxWidth - bWidthMeasure - (isTask ? 5 : 0);

                    foreach (var b in listItem)
                    {
                        ly += ProcessBlock(b, bx, ly, bWidth, nestingLevel + 1);
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
            int ch = lines * 8 + 10;
            Children.Add(new PanelElement { X = x, Y = y, Width = maxWidth, Height = ch, BackgroundColor = new Color32(255, 30, 30, 30) });
            Children.Add(new LabelElement { Text = codeText, X = x + 5, Y = y + 5, TextColor = new Color32(255, 0, 255, 0), ZIndex = 1 });
            return ch;
        }
        else if (block is CodeBlock inlineCodeBlock)
        {
            string codeText = string.Join("\n", inlineCodeBlock.Lines.Lines.Select(l => l.ToString()));
            int lines = inlineCodeBlock.Lines.Count;
            int ch = lines * 8 + 10;
            Children.Add(new PanelElement { X = x, Y = y, Width = maxWidth, Height = ch, BackgroundColor = new Color32(255, 30, 30, 30) });
            Children.Add(new LabelElement { Text = codeText, X = x + 5, Y = y + 5, TextColor = new Color32(255, 0, 255, 0), ZIndex = 1 });
            return ch;
        }
        else if (block is ThematicBreakBlock)
        {
            Children.Add(new PanelElement { X = x, Y = y + 5, Width = maxWidth, Height = 2, BackgroundColor = new Color32(255, 100, 100, 100) });
            return 12;
        }
        else if (block is HtmlBlock htmlBlock)
        {
            string htmlText = string.Join("\n", htmlBlock.Lines.Lines.Select(l => l.ToString()));
            

            // Check for tags
            var tagMatch = System.Text.RegularExpressions.Regex.Match(htmlText, @"<(h[1-6]|p|div)([^>]*)>(.*?)<\/\1>", System.Text.RegularExpressions.RegexOptions.Singleline | System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (tagMatch.Success)
            {
                string tag = tagMatch.Groups[1].Value.ToLower();
                string attrs = tagMatch.Groups[2].Value;
                string content = tagMatch.Groups[3].Value;
                
                int scale = 1;
                if (tag.StartsWith("h"))
                {
                    if (int.TryParse(tag.Substring(1), out int level))
                        scale = Math.Max(1, 4 - level + 1); // h1=4, h2=3, h3=2, h4-h6=1
                }
                
                var flow = new FlowPanelElement { X = x, Y = y, Width = maxWidth, HorizontalSpacing = 0, VerticalSpacing = 4 };
                
                var alignMatch = System.Text.RegularExpressions.Regex.Match(attrs, @"align\s*=\s*[""'](.*?)[""']", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (alignMatch.Success) flow.Align = alignMatch.Groups[1].Value.ToLower();
                
                var pipeline = new Markdig.MarkdownPipelineBuilder().UseAdvancedExtensions().Build();
                var subDoc = Markdig.Markdown.Parse(content, pipeline);
                foreach (var b in subDoc)
                {
                    if (b is Markdig.Syntax.ParagraphBlock pb) 
                    {
                        ProcessInlines(pb.Inline, flow, scale, tag.StartsWith("h"));
                    }
                    else if (b is Markdig.Syntax.HtmlBlock hb)
                    {
                        string subHtml = string.Join("\n", hb.Lines.Lines.Select(l => l.ToString()));
                        var subImgMatches = System.Text.RegularExpressions.Regex.Matches(subHtml, @"<img\s+[^>]*>", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                        foreach (System.Text.RegularExpressions.Match m in subImgMatches)
                        {
                            var srcMatch = System.Text.RegularExpressions.Regex.Match(m.Value, @"src\s*=\s*[""'](.*?)[""']", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                            var widthMatch = System.Text.RegularExpressions.Regex.Match(m.Value, @"width\s*=\s*[""'](\d+)[""']", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                            var heightMatch = System.Text.RegularExpressions.Regex.Match(m.Value, @"height\s*=\s*[""'](\d+)[""']", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                            if (srcMatch.Success)
                            {
                                int reqWidth = widthMatch.Success ? int.Parse(widthMatch.Groups[1].Value) : 0;
                                int reqHeight = heightMatch.Success ? int.Parse(heightMatch.Groups[1].Value) : 0;
                                RenderImage(srcMatch.Groups[1].Value, flow, maxWidth, reqWidth, reqHeight);
                            }
                        }
                    }
                }
                
                if (flow.Children.Count == 0) ProcessInlineText(content, flow, scale, tag.StartsWith("h"), false, null);

                int flowHeight = flow.MeasureTotalHeight();
                flow.Height = flowHeight;
                Children.Add(flow);
                return flowHeight;
            }

            // If it's just a closing tag or an opening tag without content (and not handled above)
            var loneTagMatch = System.Text.RegularExpressions.Regex.Match(htmlText.Trim(), @"^<\/?(div|p|h[1-6]|br|hr)[^>]*>$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (loneTagMatch.Success)
            {
                return 0; // Just ignore it quietly
            }

            var imgMatches = System.Text.RegularExpressions.Regex.Matches(htmlText, @"<img\s+[^>]*>", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (imgMatches.Count > 0)
            {
                var flow = new FlowPanelElement { X = x, Y = y, Width = maxWidth, HorizontalSpacing = 0, VerticalSpacing = 4 };
                foreach (System.Text.RegularExpressions.Match m in imgMatches)
                {
                    var srcMatch = System.Text.RegularExpressions.Regex.Match(m.Value, @"src\s*=\s*[""'](.*?)[""']", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                    var widthMatch = System.Text.RegularExpressions.Regex.Match(m.Value, @"width\s*=\s*[""'](\d+)[""']", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                    var heightMatch = System.Text.RegularExpressions.Regex.Match(m.Value, @"height\s*=\s*[""'](\d+)[""']", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                    if (srcMatch.Success)
                    {
                        int reqWidth = widthMatch.Success ? int.Parse(widthMatch.Groups[1].Value) : 0;
                        int reqHeight = heightMatch.Success ? int.Parse(heightMatch.Groups[1].Value) : 0;
                        RenderImage(srcMatch.Groups[1].Value, flow, maxWidth, reqWidth, reqHeight);
                    }
                }
                if (flow.Children.Count > 0)
                {
                    int flowHeight = flow.MeasureTotalHeight();
                    flow.Height = flowHeight;
                    Children.Add(flow);
                    return flowHeight;
                }
            }

            var fallbackFlow = new FlowPanelElement { X = x, Y = y, Width = maxWidth, HorizontalSpacing = 0, VerticalSpacing = 4 };
            ProcessInlineText($"[HTML: {htmlText}]", fallbackFlow, 1, false, false, new Color32(255, 100, 100, 100));
            int fallbackHeight = fallbackFlow.MeasureTotalHeight();
            fallbackFlow.Height = fallbackHeight;
            Children.Add(fallbackFlow);
            return fallbackHeight;
        }
        else if (block is MathBlock mathBlock)
        {
            string mathText = string.Join("\n", mathBlock.Lines.Lines.Select(l => l.ToString()));
            var mathEl = new MathElement { MathText = mathText, X = x, Y = y, Width = maxWidth, IsBlock = true };
            Children.Add(mathEl);
            return mathEl.MeasureHeight();
        }
        else if (block is Markdig.Extensions.Tables.Table table)
        {
            int ty = y;
            bool isHeader = true;
            foreach (var row in table)
            {
                if (row is Markdig.Extensions.Tables.TableRow tableRow)
                {
                    int tx = x;
                    int cellWidth = maxWidth / Math.Max(1, tableRow.Count);
                    int maxCellHeight = 0;
                    
                    if (isHeader)
                    {
                        // Draw header row background
                        Children.Add(new PanelElement { X = x, Y = ty, Width = maxWidth, Height = 10, BackgroundColor = new Color32(255, 40, 40, 50) });
                    }

                    foreach (var cell in tableRow)
                    {
                        if (cell is Markdig.Extensions.Tables.TableCell tableCell)
                        {
                            int cy = ty;
                            foreach (var b in tableCell)
                            {
                                cy += ProcessBlock(b, tx + 5, cy + 5, cellWidth - 10, nestingLevel);
                            }
                            if (cy - ty > maxCellHeight) maxCellHeight = cy - ty;
                        }
                        
                        // Vertical divider
                        Children.Add(new PanelElement { X = tx, Y = ty, Width = 1, Height = maxCellHeight + 5, BackgroundColor = new Color32(255, 50, 50, 50) });
                        
                        tx += cellWidth;
                    }
                    
                    // Final right border
                    Children.Add(new PanelElement { X = x + maxWidth - 1, Y = ty, Width = 1, Height = maxCellHeight + 5, BackgroundColor = new Color32(255, 50, 50, 50) });
                    
                    if (isHeader)
                    {
                        var bgPanel = Children.LastOrDefault(c => c is PanelElement && ((PanelElement)c).BackgroundColor.R == 40 && c.Y == ty) as PanelElement;
                        if (bgPanel != null) bgPanel.Height = maxCellHeight + 5;
                        isHeader = false;
                    }
                    
                    Children.Add(new PanelElement { X = x, Y = ty + maxCellHeight + 5, Width = maxWidth, Height = 1, BackgroundColor = new Color32(255, 50, 50, 50) });
                    ty += maxCellHeight + 6;
                }
            }
            return ty - y;
        }
        else if (block.GetType().Name == "FootnoteGroup")
        {
            Children.Add(new PanelElement { X = x, Y = y + 5, Width = maxWidth / 2, Height = 1, BackgroundColor = new Color32(255, 100, 100, 100) });
            int fy = y + 15;
            if (block is ContainerBlock containerBlock)
            {
                foreach (var child in containerBlock)
                {
                    fy += ProcessBlock(child, x, fy, maxWidth, nestingLevel);
                    fy += 5;
                }
            }
            return fy - y;
        }
        
        // Failsafe for ANY unrecognized block
        else
        {
            string rawContent = block.ToString() ?? "Unknown Block";
            var flow = new FlowPanelElement { X = x, Y = y, Width = maxWidth, HorizontalSpacing = 0, VerticalSpacing = 4 };
            ProcessInlineText($"[{rawContent}]", flow, 1, false, false, new Color32(255, 200, 0, 0));
            
            int flowHeight = flow.MeasureTotalHeight();
            flow.Height = flowHeight;
            Children.Add(flow);
            return flowHeight;
        }
    }

    private void RenderImage(string? url, FlowPanelElement flow, int maxWidth, int reqWidth = 0, int reqHeight = 0)
    {
        if (string.IsNullOrEmpty(url)) return;
        try
        {
            VirtualScreen? vs = null;
            if (url.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                url = url.Replace("github.com", "raw.githubusercontent.com").Replace("/blob/", "/");
                using var client = new HttpClient();
                // Add user agent just in case some servers require it
                client.DefaultRequestHeaders.Add("User-Agent", "AurSh-Browser/1.0");
                var bytes = client.GetByteArrayAsync(url).Result;
                
                bool isSvg = false, isPng = false, isJpg = false, isBmp = false;
                if (bytes.Length >= 8 && bytes[0] == 137 && bytes[1] == 80 && bytes[2] == 78 && bytes[3] == 71 && bytes[4] == 13 && bytes[5] == 10 && bytes[6] == 26 && bytes[7] == 10)
                    isPng = true;
                else if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xD8)
                    isJpg = true;
                else if (bytes.Length >= 2 && bytes[0] == 0x42 && bytes[1] == 0x4D)
                    isBmp = true;
                else
                {
                    string text = System.Text.Encoding.UTF8.GetString(bytes, 0, Math.Min(bytes.Length, 1024));
                    if (text.Contains("<svg", StringComparison.OrdinalIgnoreCase))
                        isSvg = true;
                }

                string ext = isSvg ? ".svg" : (isPng ? ".png" : (isBmp ? ".bmp" : ".jpg"));
                string tempFile = Path.GetTempFileName() + ext;
                File.WriteAllBytes(tempFile, bytes);
                
                if (isSvg) vs = SvgDecoder.Decode(tempFile);
                else if (isPng) vs = PngDecoder.Decode(tempFile);
                else if (isBmp) vs = BmpDecoder.Decode(tempFile);
                else vs = JpgDecoder.Decode(tempFile);
            }
            else
            {
                string absolutePath = url;
                if (!Path.IsPathRooted(url))
                {
                    absolutePath = Path.Combine(BasePath, url.TrimStart('/', '\\'));
                }

                if (File.Exists(absolutePath))
                {
                    if (absolutePath.EndsWith(".svg", StringComparison.OrdinalIgnoreCase))
                        vs = SvgDecoder.Decode(absolutePath);
                    else
                        vs = JpgDecoder.Decode(absolutePath);
                }
            }

            if (vs != null)
            {
                int finalWidth = reqWidth > 0 ? reqWidth : vs.Width;
                int finalHeight = reqHeight > 0 ? reqHeight : vs.Height;
                var imgEl = new ImageElement { Image = vs, Width = finalWidth, Height = finalHeight };
                flow.Children.Add(imgEl);
            }
            else
            {
                flow.Children.Add(new LabelElement { Text = $"[Image Not Found: {url}]", TextColor = new Color32(255, 255, 0, 0) });
            }
        }
        catch (Exception ex)
        {
            flow.Children.Add(new LabelElement { Text = $"[Img Error: {ex.Message}]", TextColor = new Color32(255, 255, 0, 0) });
        }
    }

    private void ProcessInlineText(string text, FlowPanelElement flow, int scale, bool bold, bool italic, Color32? color, bool strikethrough = false, bool underline = false, Color32? bgColor = null, int yOffset = 0)
    {
        int start = 0;
        while (start < text.Length)
        {
            int nextSpace = text.IndexOf(' ', start);
            if (nextSpace == -1)
            {
                var lbl = new LabelElement { Text = text.Substring(start), Scale = scale, Bold = bold, Italic = italic, TextColor = color ?? Color32.White, Strikethrough = strikethrough, Underline = underline, BackgroundColor = bgColor };
                if (yOffset != 0) lbl.Y += yOffset;
                flow.Children.Add(lbl);
                break;
            }
            else
            {
                var lbl = new LabelElement { Text = text.Substring(start, nextSpace - start + 1), Scale = scale, Bold = bold, Italic = italic, TextColor = color ?? Color32.White, Strikethrough = strikethrough, Underline = underline, BackgroundColor = bgColor };
                if (yOffset != 0) lbl.Y += yOffset;
                flow.Children.Add(lbl);
                start = nextSpace + 1;
            }
        }
    }

    private void ProcessInlines(ContainerInline? inlines, FlowPanelElement flow, int scale, bool initBold, Color32? initColor = null, bool initItalic = false, bool initStrikethrough = false, bool initUnderline = false, Color32? initBgColor = null, int initYOffset = 0)
    {
        if (inlines == null) return;

        bool bold = initBold;
        bool italic = initItalic;
        bool strikethrough = initStrikethrough;
        bool underline = initUnderline;
        Color32? color = initColor;
        Color32? bgColor = initBgColor;
        int yOffset = initYOffset;

        foreach (var inline in inlines)
        {
            if (inline is LiteralInline literal)
            {
                string text = literal.Content.ToString();
                ProcessInlineText(text, flow, scale, bold, italic, color, strikethrough, underline, bgColor, yOffset);
            }
            else if (inline is EmphasisInline emphasis)
            {
                if (emphasis.DelimiterChar == '*' || emphasis.DelimiterChar == '_')
                {
                    if (emphasis.DelimiterCount >= 2)
                        ProcessInlines(emphasis, flow, scale, true, color, italic, strikethrough, underline, bgColor, yOffset);
                    else
                        ProcessInlines(emphasis, flow, scale, bold, color, true, strikethrough, underline, bgColor, yOffset);
                }
                else if (emphasis.DelimiterChar == '~')
                {
                    if (emphasis.DelimiterCount >= 2)
                        ProcessInlines(emphasis, flow, scale, bold, color, italic, true, underline, bgColor, yOffset); // strikethrough
                    else
                        ProcessInlines(emphasis, flow, scale, bold, color, italic, strikethrough, underline, bgColor, yOffset + 2); // subscript
                }
                else if (emphasis.DelimiterChar == '^')
                {
                    ProcessInlines(emphasis, flow, scale, bold, color, italic, strikethrough, underline, bgColor, yOffset - 2); // superscript
                }
                else if (emphasis.DelimiterChar == '+')
                {
                    ProcessInlines(emphasis, flow, scale, bold, color, italic, strikethrough, true, bgColor, yOffset); // underline
                }
                else if (emphasis.DelimiterChar == '=')
                {
                    ProcessInlines(emphasis, flow, scale, bold, color, italic, strikethrough, underline, new Color32(255, 100, 100, 0), yOffset); // marked/highlight
                }
            }
            else if (inline is HtmlInline htmlInline)
            {
                string tag = htmlInline.Tag.ToLower();
                if (tag.StartsWith("<b") || tag.StartsWith("<strong")) bold = true;
                else if (tag.StartsWith("</b") || tag.StartsWith("</strong")) bold = initBold;
                
                else if (tag.StartsWith("<i") || tag.StartsWith("<em")) italic = true;
                else if (tag.StartsWith("</i") || tag.StartsWith("</em")) italic = initItalic;
                
                else if (tag.StartsWith("<u")) underline = true;
                else if (tag.StartsWith("</u")) underline = initUnderline;
                
                else if (tag.StartsWith("<s") || tag.StartsWith("<strike") || tag.StartsWith("<del")) strikethrough = true;
                else if (tag.StartsWith("</s") || tag.StartsWith("</strike") || tag.StartsWith("</del")) strikethrough = initStrikethrough;
                
                else if (tag.StartsWith("<br")) flow.Children.Add(new PanelElement { Width = 9999, Height = 0 });
                else if (tag.StartsWith("<hr")) flow.Children.Add(new PanelElement { Width = 9999, Height = 2 });
                else if (tag.StartsWith("<img"))
                {
                    var srcMatch = System.Text.RegularExpressions.Regex.Match(htmlInline.Tag, @"src\s*=\s*[""'](.*?)[""']", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                    var widthMatch = System.Text.RegularExpressions.Regex.Match(htmlInline.Tag, @"width\s*=\s*[""'](\d+)[""']", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                    var heightMatch = System.Text.RegularExpressions.Regex.Match(htmlInline.Tag, @"height\s*=\s*[""'](\d+)[""']", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                    if (srcMatch.Success)
                    {
                        int reqWidth = widthMatch.Success ? int.Parse(widthMatch.Groups[1].Value) : 0;
                        int reqHeight = heightMatch.Success ? int.Parse(heightMatch.Groups[1].Value) : 0;
                        RenderImage(srcMatch.Groups[1].Value, flow, 1000, reqWidth, reqHeight);
                    }
                }
            }
            else if (inline is LinkInline link)
            {
                if (link.IsImage)
                {
                    RenderImage(link.Url, flow, 1000);
                }
                else
                {
                    ProcessInlines(link, flow, scale, bold, new Color32(255, 100, 150, 255), italic, strikethrough, underline, bgColor, yOffset);
                    var lbl = new LabelElement { Text = $"[{link.Url}] ", Scale = scale, Bold = bold, Italic = italic, Strikethrough = strikethrough, Underline = underline, BackgroundColor = bgColor, TextColor = new Color32(255, 100, 100, 255) };
                    if (yOffset != 0) lbl.Y += yOffset;
                    flow.Children.Add(lbl);
                }
            }
            else if (inline is CodeInline codeInline)
            {
                var lbl = new LabelElement { Text = $"`{codeInline.Content}`", Scale = scale, Bold = bold, Italic = italic, Strikethrough = strikethrough, Underline = underline, BackgroundColor = new Color32(255, 30, 30, 30), TextColor = new Color32(255, 0, 255, 0) };
                if (yOffset != 0) lbl.Y += yOffset;
                flow.Children.Add(lbl);
            }
            else if (inline is MathInline mathInline)
            {
                var lbl = new LabelElement { Text = $" {mathInline.Content} ", Scale = scale, Bold = false, Italic = true, BackgroundColor = new Color32(255, 20, 40, 40), TextColor = new Color32(255, 100, 255, 255) };
                if (yOffset != 0) lbl.Y += yOffset;
                flow.Children.Add(lbl);
            }
            else if (inline is LineBreakInline)
            {
                flow.Children.Add(new PanelElement { Width = 9999, Height = 0 });
            }
            else if (inline is ContainerInline container)
            {
                ProcessInlines(container, flow, scale, bold, color, italic, strikethrough, underline, bgColor, yOffset);
            }
        }
    }
}
