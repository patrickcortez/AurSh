using AurShell.Utils;

namespace AurShell.BlackBoxView;

public static class BlackBoxDemo
{
    public static int Run(string[] args)
    {
        BoxStyle? overrideStyle = null;
        foreach (string arg in args)
        {
            string a = arg.Trim().ToLowerInvariant();
            switch (a)
            {
                case "rounded":
                case "square":
                case "ascii":
                    overrideStyle = BoxChars.ParseStyle(a);
                    break;
            }
        }

        var writer = System.Console.Out;
        var config = BlackBoxConfig.FromEnvironment();
        if (overrideStyle is BoxStyle s)
            config.Border = s;

        var box = new BlackBox(config);

        writer.WriteLine();
        writer.WriteLine(Section("Demo 1: empty box (no body)"));
        RenderScene(box, "demo", "", writer, lines: new (string, LineKind)[0], exitCode: null, simulateElapsedMs: 0);

        writer.WriteLine();
        writer.WriteLine(Section("Demo 2: python REPL (matches spec mockup)"));
        RenderScene(box, "python", "python",
            writer,
            lines: new (string, LineKind)[]
            {
                ("Python 3.14.0", LineKind.Stdout),
                (">>> print(\"Hello AurSh\")", LineKind.Stdout),
                ("Hello AurSh", LineKind.Stdout),
                (">>> exit()", LineKind.Stdout)
            },
            exitCode: null,
            simulateElapsedMs: 1234);

        writer.WriteLine();
        writer.WriteLine(Section("Demo 3: pipeline (cmd1 | grep ...)"));
        RenderScene(box,
            "git log --oneline | grep blackbox",
            "git log | grep",
            writer,
            lines: new (string, LineKind)[]
            {
                ("5a045e6 added Blackbox dir", LineKind.Stdout),
                ("c084fd8 BlackBox: scaffolding for execution viewport", LineKind.Stdout)
            },
            exitCode: 0,
            simulateElapsedMs: 312);

        writer.WriteLine();
        writer.WriteLine(Section("Demo 4: stderr interleaved"));
        RenderScene(box, "make build", "make",
            writer,
            lines: new (string, LineKind)[]
            {
                ("cc -c main.c", LineKind.Stdout),
                ("main.c:42:5: warning: unused variable \u2018x\u2019", LineKind.Stderr),
                ("cc -c lib.c", LineKind.Stdout),
                ("lib.c:10:3: error: expected \u2018;\u2019 before \u2018}\u2019 token", LineKind.Stderr),
                ("make: *** [Makefile:7: lib.o] Error 1", LineKind.Stderr)
            },
            exitCode: 2,
            simulateElapsedMs: 4500);

        writer.WriteLine();
        writer.WriteLine(Section("Demo 5: overflow + scroll indicator"));
        var manyLines = new List<(string, LineKind)>();
        for (int i = 1; i <= 50; i++)
            manyLines.Add(($"line {i,3}: lorem ipsum dolor sit amet consectetur adipiscing elit", LineKind.Stdout));
        RenderScene(box, "cat /tmp/big.log", "cat",
            writer,
            lines: manyLines.ToArray(),
            exitCode: 0,
            simulateElapsedMs: 12);

        writer.WriteLine();
        writer.WriteLine(Section("Demo 6: style fallbacks"));
        foreach (var style in new[] { BoxStyle.Rounded, BoxStyle.Square, BoxStyle.Ascii })
        {
            var c2 = BlackBoxConfig.FromEnvironment();
            c2.Border = style;
            var box2 = new BlackBox(c2);
            writer.WriteLine($"  {Ansi.Dim}({style.ToString().ToLowerInvariant()}){Ansi.Reset}");
            RenderScene(box2, "echo hi", "echo",
                writer,
                lines: new (string, LineKind)[] { ("hi", LineKind.Stdout) },
                exitCode: 0,
                simulateElapsedMs: 3);
        }

        writer.WriteLine();
        writer.WriteLine($"{Ansi.Dim}--- end of aursh-blackbox-demo ---{Ansi.Reset}");
        writer.WriteLine();
        return 0;
    }

    private static void RenderScene(
        BlackBox box,
        string commandLine,
        string commandTitle,
        System.IO.TextWriter writer,
        (string, LineKind)[] lines,
        int? exitCode,
        int simulateElapsedMs)
    {
        using var session = box.Open(commandLine, commandTitle, System.Environment.CurrentDirectory);
        foreach (var (text, kind) in lines)
            session.Buffer.Append(text, kind);

        if (exitCode is int code)
        {
            session.SetExitCode(code);
        }

        _ = simulateElapsedMs;

        box.Repaint(session, writer);
    }

    private static string Section(string title)
    {
        return $"{Ansi.Bold}{Ansi.FgBrightMagenta}\u25b6 {title}{Ansi.Reset}";
    }
}
