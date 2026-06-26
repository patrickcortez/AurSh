using System;
using System.IO;
using AurShell.Core;
using AurShell.Parser;

namespace AurShell.Commands;

public static class AurshViewCommand
{
    public static int Execute(SimpleCommandNode cmd, ShellEnvironment env, string workingDirectory)
    {
        if (cmd.Args.Count == 0)
        {
            Console.Error.WriteLine("aursh: aursh-view: missing file operand");
            return 1;
        }

        string targetFile = Utils.FileSystem.ResolvePath(cmd.Args[0], workingDirectory);
        if (!System.IO.File.Exists(targetFile))
        {
            Console.Error.WriteLine($"aursh: aursh-view: cannot access '{targetFile}': No such file or directory");
            return 1;
        }

        string ext = System.IO.Path.GetExtension(targetFile).ToLowerInvariant();
        if (ext == ".md")
        {
            try
            {
                int windowWidth = 800;
                int windowHeight = 600;

                AurShell.Graphics.Compositor compositor = new AurShell.Graphics.Compositor(windowWidth, windowHeight);
                compositor.BackgroundColor = new AurShell.Graphics.Color32(255, 30, 30, 30);

                var scrollView = new AurShell.Graphics.UI.ScrollViewerElement { X = 0, Y = 0, Width = windowWidth, Height = windowHeight, ZIndex = 0 };
                var mdElem = new AurShell.Graphics.UI.MarkdownElement { X = 15, Y = 10, Width = windowWidth - 30, ZIndex = 0 };
                mdElem.BasePath = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(targetFile)) ?? System.Environment.CurrentDirectory;
                mdElem.MarkdownText = System.IO.File.ReadAllText(targetFile);
                scrollView.Content.Children.Add(mdElem);
                compositor.AddElement(scrollView);

                using (var host = new AurShell.Graphics.SdlWindowHost(windowWidth, windowHeight, $"Markdown Viewer - {System.IO.Path.GetFileName(targetFile)}"))
                {
                    host.Show(compositor);
                }

                return 0;
            }
            catch (System.Exception ex)
            {
                Console.Error.WriteLine($"aursh: aursh-view: Error rendering markdown viewer - {ex.Message}");
                return 1;
            }
        }

        AurShell.Graphics.VirtualScreen imageBuffer;
        try
        {
            imageBuffer = ext switch
            {
                ".bmp" => AurShell.Graphics.BmpDecoder.Decode(targetFile),
                ".jpg" or ".jpeg" => AurShell.Graphics.JpgDecoder.Decode(targetFile),
                ".svg" => AurShell.Graphics.SvgDecoder.Decode(targetFile),
                _ => AurShell.Graphics.PngDecoder.Decode(targetFile) // default to png
            };
        }
        catch (System.Exception ex)
        {
            Console.Error.WriteLine($"aursh: aursh-view: Error decoding image '{System.IO.Path.GetFileName(targetFile)}' - {ex.Message}");
            return 1;
        }

        try
        {
            int windowWidth = imageBuffer.Width + 40;
            int windowHeight = imageBuffer.Height + 80;

            AurShell.Graphics.Compositor compositor = new AurShell.Graphics.Compositor(windowWidth, windowHeight);
            compositor.BackgroundColor = new AurShell.Graphics.Color32(255, 30, 30, 30);

            AurShell.Graphics.WindowElement win = new AurShell.Graphics.WindowElement
            {
                X = 10,
                Y = 10,
                Width = imageBuffer.Width + 20,
                Height = imageBuffer.Height + 50,
                ZIndex = 1,
                Title = $"Image Viewer - {System.IO.Path.GetFileName(targetFile)}"
            };

            AurShell.Graphics.ImageElement img = new AurShell.Graphics.ImageElement
            {
                X = 20,
                Y = 40,
                ZIndex = 2,
                Image = imageBuffer
            };

            compositor.AddElement(win);
            compositor.AddElement(img);

            using (var host = new AurShell.Graphics.SdlWindowHost(windowWidth, windowHeight, "AurSh Native Window Viewer"))
            {
                host.Show(compositor);
            }

            return 0;
        }
        catch (System.Exception ex)
        {
            Console.Error.WriteLine($"aursh: aursh-view: Error rendering image viewer - {ex.Message}");
            return 1;
        }
    }

}

