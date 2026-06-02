using System;
using System.IO;
using StbImageSharp;

namespace AurShell.Graphics;

public static class JpgDecoder
{
    public static VirtualScreen Decode(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        ImageResult image = ImageResult.FromStream(stream, ColorComponents.RedGreenBlueAlpha);
        
        VirtualScreen screen = new VirtualScreen(image.Width, image.Height);
        
        for (int y = 0; y < image.Height; y++)
        {
            for (int x = 0; x < image.Width; x++)
            {
                int i = (y * image.Width + x) * 4;
                byte r = image.Data[i];
                byte g = image.Data[i + 1];
                byte b = image.Data[i + 2];
                byte a = image.Data[i + 3];
                
                screen.SetPixel(x, y, new Color32(a, r, g, b));
            }
        }
        
        return screen;
    }
}
