using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Input;
using AurShell.ISE.Services;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using AvaloniaEdit.CodeCompletion;
using AvaloniaEdit.Highlighting;
using AvaloniaEdit.Highlighting.Xshd;

namespace AurShell.ISE;

public partial class MainWindow : Window
{
    private readonly AurshRunner _runner;
    private CompletionWindow? _completionWindow;
    private List<string> _knownCommands = new();

    public MainWindow()
    {
        InitializeComponent();
        _runner = new AurshRunner();

        LoadCommands();
        ApplyDynamicSyntaxHighlighting();

        Editor.TextArea.TextEntered += TextArea_TextEntered;
        Editor.TextArea.TextEntering += TextArea_TextEntering;
    }

    private void LoadCommands()
    {
        try
        {
            string aurshPath = "aursh";
            string exeDir = AppContext.BaseDirectory;
            if (File.Exists(Path.Combine(exeDir, "aursh.exe")))
                aurshPath = Path.Combine(exeDir, "aursh.exe");
            else if (File.Exists(Path.Combine(exeDir, "aursh")))
                aurshPath = Path.Combine(exeDir, "aursh");

            var psi = new ProcessStartInfo(aurshPath, "--dump-commands")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var proc = Process.Start(psi);
            if (proc != null)
            {
                string json = proc.StandardOutput.ReadToEnd().Trim();
                proc.WaitForExit();
                if (json.StartsWith("[") && json.EndsWith("]"))
                {
                    var builtins = json.Trim('[', ']').Split(',')
                                       .Select(s => s.Trim().Trim('"'))
                                       .Where(s => !string.IsNullOrEmpty(s));
                    _knownCommands.AddRange(builtins);
                }
            }
        }
        catch { }

        try
        {
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string busyboxPath = Path.Combine(home, ".aursh", "bin", OperatingSystem.IsWindows() ? "busybox.exe" : "busybox");
            if (File.Exists(busyboxPath))
            {
                var psi = new ProcessStartInfo(busyboxPath, "--list")
                {
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var proc = Process.Start(psi);
                if (proc != null)
                {
                    string output = proc.StandardOutput.ReadToEnd();
                    proc.WaitForExit();
                    var commands = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                    _knownCommands.AddRange(commands);
                }
            }
        }
        catch { }

        _knownCommands = _knownCommands.Distinct().OrderBy(c => c).ToList();
    }

    private void ApplyDynamicSyntaxHighlighting()
    {
        if (_knownCommands.Count == 0) return;

        var sb = new StringBuilder();
        sb.AppendLine("<?xml version=\"1.0\"?>");
        sb.AppendLine("<SyntaxDefinition name=\"AurSh\" xmlns=\"http://icsharpcode.net/sharpdevelop/syntaxdefinition/2008\">");
        sb.AppendLine("  <Color name=\"Keyword\" foreground=\"#bb9af7\" fontWeight=\"bold\" />");
        sb.AppendLine("  <Color name=\"String\" foreground=\"#9ece6a\" />");
        sb.AppendLine("  <Color name=\"Comment\" foreground=\"#565f89\" />");
        sb.AppendLine("  <RuleSet>");
        sb.AppendLine("    <Span color=\"String\">");
        sb.AppendLine("      <Begin>\"</Begin>");
        sb.AppendLine("      <End>\"</End>");
        sb.AppendLine("    </Span>");
        sb.AppendLine("    <Span color=\"String\">");
        sb.AppendLine("      <Begin>'</Begin>");
        sb.AppendLine("      <End>'</End>");
        sb.AppendLine("    </Span>");
        sb.AppendLine("    <Rule color=\"Comment\">#.*</Rule>");
        sb.AppendLine("    <Keywords color=\"Keyword\">");
        foreach (var cmd in _knownCommands)
        {
            string safeCmd = cmd.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
            sb.AppendLine($"      <Word>{safeCmd}</Word>");
        }
        sb.AppendLine("    </Keywords>");
        sb.AppendLine("  </RuleSet>");
        sb.AppendLine("</SyntaxDefinition>");

        using var reader = new XmlTextReader(new StringReader(sb.ToString()));
        Editor.SyntaxHighlighting = HighlightingLoader.Load(reader, HighlightingManager.Instance);
    }

    private void TextArea_TextEntered(object? sender, TextInputEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(e.Text)) return;
        if (!char.IsLetter(e.Text[0])) return;

        if (_completionWindow == null)
        {
            _completionWindow = new CompletionWindow(Editor.TextArea);
            _completionWindow.Closed += (o, args) => _completionWindow = null;

            var data = _completionWindow.CompletionList.CompletionData;
            
            int offset = Editor.TextArea.Caret.Offset;
            int startOffset = offset - 1;
            while (startOffset > 0 && char.IsLetterOrDigit(Editor.Document.GetCharAt(startOffset - 1)))
                startOffset--;
            
            string currentWord = Editor.Document.GetText(startOffset, offset - startOffset).ToLower();

            foreach (var cmd in _knownCommands)
            {
                if (cmd.ToLower().StartsWith(currentWord))
                {
                    data.Add(new AurshCompletionData(cmd));
                }
            }

            if (data.Count > 0)
                _completionWindow.Show();
            else
                _completionWindow.Close();
        }
    }

    private void TextArea_TextEntering(object? sender, TextInputEventArgs e)
    {
        if (e.Text != null && e.Text.Length > 0 && _completionWindow != null)
        {
            if (!char.IsLetterOrDigit(e.Text[0]))
            {
                _completionWindow.CompletionList.RequestInsertion(e);
            }
        }
    }

    private async void RunButton_Click(object? sender, RoutedEventArgs e)
    {
        RunButton.IsEnabled = false;
        string script = Editor.Text ?? "";
        
        OutputText.Text = "Running script...\n";
        
        string result = await _runner.RunScriptAsync(script);
        OutputText.Text = result;
        
        RunButton.IsEnabled = true;
    }

    private void ClearButton_Click(object? sender, RoutedEventArgs e)
    {
        OutputText.Text = "";
    }
}