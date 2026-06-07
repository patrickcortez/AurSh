using System;
using System.IO;
using Svg.Skia;
using SkiaSharp;

namespace AurShell.Graphics;

public static class SvgDecoder
{
    public static VirtualScreen Decode(string filePath)
    {
        var svg = new SKSvg();
        svg.Load(filePath);

        if (svg.Picture == null)
        {
            throw new Exception("Could not load SVG picture.");
        }

        int width = (int)Math.Ceiling(svg.Picture.CullRect.Width);
        int height = (int)Math.Ceiling(svg.Picture.CullRect.Height);

        // Sanity check for SVG bounds
        if (width <= 0 || height <= 0)
        {
            width = 800;
            height = 600;
        }
        else if (width > 4000 || height > 4000)
        {
            // Simple hard cap for massive SVGs to prevent memory issues
            float scale = Math.Min(4000f / width, 4000f / height);
            width = (int)(width * scale);
            height = (int)(height * scale);
        }

        var bitmap = new SKBitmap(width, height);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.Transparent);

            // Adjust matrix if we scaled it down
            if (width != (int)Math.Ceiling(svg.Picture.CullRect.Width))
            {
                float scaleX = (float)width / svg.Picture.CullRect.Width;
                float scaleY = (float)height / svg.Picture.CullRect.Height;
                canvas.Scale(scaleX, scaleY);
            }

            canvas.DrawPicture(svg.Picture);
        }

        VirtualScreen screen = new VirtualScreen(width, height);

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                var color = bitmap.GetPixel(x, y);
                screen.SetPixel(x, y, new Color32(color.Alpha, color.Red, color.Green, color.Blue));
            }
        }

        return screen;
    }
}
