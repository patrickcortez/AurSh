using System;
using System.IO;
using AurShell.Core;
using System.Linq;

class TokenPrinter {
    static void Main(string[] args) {
        var env = new ShellEnvironment();
        string input = File.ReadAllText(args[0]);
        var lexer = new Lexer(input, env);
        var tokens = lexer.Tokenize();
        foreach(var t in tokens) {
            Console.WriteLine($"Token: {t.Type}, Value: '{t.Value}', Raw: '{t.RawExpandedValue}', WasQuoted: {t.WasQuoted}");
        }
    }
}
