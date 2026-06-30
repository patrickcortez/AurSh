using Avalonia.Controls;
using Avalonia.Interactivity;
using AurShell.ISE.Services;

namespace AurShell.ISE;

public partial class MainWindow : Window
{
    private readonly AurshRunner _runner;

    public MainWindow()
    {
        InitializeComponent();
        _runner = new AurshRunner();
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