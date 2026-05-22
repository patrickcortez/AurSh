using AurShell.Utils;
using System.Text.Json;
using System.Text.Json.Serialization;
namespace AurShell.BlackBoxView;

[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNameCaseInsensitive = true,
    AllowTrailingCommas = true,
    ReadCommentHandling = JsonCommentHandling.Skip)]
[JsonSerializable(typeof(BlackBoxConfig.BlackBoxConfigFile))]
internal partial class BlackBoxConfigJsonContext : JsonSerializerContext
{
}

public sealed class BlackBoxConfig
{
    public BoxStyle Border { get; set; } = BoxStyle.Rounded;
    public string BorderColor { get; set; } = Ansi.FgBrightBlack;
    public string TitleColor { get; set; } = Ansi.FgBrightCyan;
    public string MetaColor { get; set; } = Ansi.FgBrightBlack;
    public string StderrColor { get; set; } = Ansi.FgBrightRed;
    public string ExitOkColor { get; set; } = Ansi.FgBrightGreen;
    public string ExitErrColor { get; set; } = Ansi.FgBrightRed;

    public string Title { get; set; } = "BlackBox";
    public string? BackgroundColor { get; set; }
    public bool ShowTitle { get; set; } = true;
    public bool Enabled { get; set; } = true;

    public int? MaxHeight { get; set; }
    public int MinHeight { get; set; } = 1;
    public int BufferLines { get; set; } = 5000;

    public bool ShowPipeInterior { get; set; }
    public bool InScripts { get; set; }

    public List<string> Bypass { get; set; } = new()
    {
        "vim", "nvim", "vi", "nano",
        "less", "more", "man",
        "top", "htop", "btop",
        "fzf", "tmux", "screen", "ssh",
        "aursh-cat","aursh-ls","aursh-history show"
    };

    // Blackbox config from Env variables handler
    public static BlackBoxConfig FromEnvironment()
    {
        var c = new BlackBoxConfig
        { // Get user declared configs from environmental variables
            Border = BoxChars.Detect(System.Environment.GetEnvironmentVariable("BLACKBOX_BORDER")),
            MaxHeight = TryParseInt(System.Environment.GetEnvironmentVariable("BLACKBOX_MAX_HEIGHT")),
            BufferLines = TryParseInt(System.Environment.GetEnvironmentVariable("BLACKBOX_BUFFER_LINES")) ?? 5000,
            ShowPipeInterior = ParseBool(System.Environment.GetEnvironmentVariable("BLACKBOX_SHOW_PIPE_INTERIOR")),
            InScripts = ParseBool(System.Environment.GetEnvironmentVariable("BLACKBOX_IN_SCRIPTS"))
        };

        int? minHeight = TryParseInt(System.Environment.GetEnvironmentVariable("BLACKBOX_MIN_HEIGHT"));
        if (minHeight is int mh && mh > 0)
            c.MinHeight = mh;

        string? bypass = System.Environment.GetEnvironmentVariable("BLACKBOX_BYPASS");
        if (!string.IsNullOrWhiteSpace(bypass))
        {
            c.Bypass = new List<string>(bypass.Split(new[] { ' ', '\t', ',' },
                System.StringSplitOptions.RemoveEmptyEntries));
        }

        LoadFromJson(c); // finally load it the json

        return c;
    }

    public int ResolveMaxHeight()
    {
        if (MaxHeight is int explicitMax && explicitMax > 0)
            return System.Math.Max(MinHeight + 2, explicitMax);

        int termHeight = Platform.TerminalHeight;
        int derived = System.Math.Min(20, System.Math.Max(MinHeight + 2, termHeight - 4));
        return derived;
    }

    // Helper methods

    private static int? TryParseInt(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        return int.TryParse(value, out int v) ? v : null;
    }

    private static bool ParseBool(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        return value.Trim().ToLowerInvariant() switch
        {
            "1" or "true" or "yes" or "on" => true,
            _ => false
        };
    }

    public class BlackBoxConfigFile
    {
        [JsonPropertyName("Blackbox Title")]
        public string? Title { get; set; }

        [JsonPropertyName("Border-Color")]
        public string? BorderColor { get; set; }

        [JsonPropertyName("Background-Color")]
        public string? BackgroundColor { get; set; }

        [JsonPropertyName("Show-Title")]
        public bool? ShowTitle { get; set; }

        [JsonPropertyName("Enabled")]
        public bool? Enabled { get; set; }

        [JsonPropertyName("Border-Style")]
        public string? BorderStyle { get; set; }
    }

    private static void LoadFromJson(BlackBoxConfig config)
    {
        try
        {
            string aurshDir = Path.Combine(Platform.HomeDirectory, ".aursh");
            if (!Directory.Exists(aurshDir)) Directory.CreateDirectory(aurshDir);

            string configPath = Path.Combine(aurshDir, "blackconfig.json");
            
            if (!File.Exists(configPath)) // if the config file does not exist, we make a new one
            {
                var defaultData = new BlackBoxConfigFile
                {
                    Title = "BlackBox",
                    BorderColor = "White",
                    BackgroundColor = "Black",
                    ShowTitle = true,
                    Enabled = true,
                    BorderStyle = "Rounded"
                };
                string defaultJson = JsonSerializer.Serialize(defaultData, BlackBoxConfigJsonContext.Default.BlackBoxConfigFile);
                File.WriteAllText(configPath, defaultJson);
            }

            string json = File.ReadAllText(configPath);

            // Pre-process to handle capitalized True/False which is common but invalid JSON
            json = System.Text.RegularExpressions.Regex.Replace(json, @":\s*True\b", ": true", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            json = System.Text.RegularExpressions.Regex.Replace(json, @":\s*False\b", ": false", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

            var fileConfig = JsonSerializer.Deserialize(json, BlackBoxConfigJsonContext.Default.BlackBoxConfigFile);

            if (fileConfig == null) return;

            if (fileConfig.Title != null) config.Title = fileConfig.Title;
            if (fileConfig.ShowTitle.HasValue) config.ShowTitle = fileConfig.ShowTitle.Value;
            if (fileConfig.Enabled.HasValue) config.Enabled = fileConfig.Enabled.Value;

            if (!string.IsNullOrWhiteSpace(fileConfig.BorderColor))
                config.BorderColor = ParseColorName(fileConfig.BorderColor, isBackground: false);

            if (!string.IsNullOrWhiteSpace(fileConfig.BackgroundColor))
                config.BackgroundColor = ParseColorName(fileConfig.BackgroundColor, isBackground: true);

            if (!string.IsNullOrWhiteSpace(fileConfig.BorderStyle))
            {
                config.Border = fileConfig.BorderStyle.ToLowerInvariant() switch
                {
                    "rounded" => BoxStyle.Rounded,
                    "ascii" => BoxStyle.Ascii,
                    "square" => BoxStyle.Square,
                    _ => config.Border
                };
            }
        }
        catch(Exception ex)
        {
            Console.Error.WriteLine($"{Ansi.FgRed}Aursh: {ex.Message} | {ex.StackTrace}");
        }
    }

    private static string ParseColorName(string name, bool isBackground) // background Colors,  its very limited for now
    {
        return name.Trim().ToLowerInvariant() switch
        {
            "black" => isBackground ? Ansi.BgBlack : Ansi.FgBlack,
            "red" => isBackground ? Ansi.BgRed : Ansi.FgRed,
            "green" => isBackground ? Ansi.BgGreen : Ansi.FgGreen,
            "yellow" => isBackground ? Ansi.BgYellow : Ansi.FgYellow,
            "blue" => isBackground ? Ansi.BgBlue : Ansi.FgBlue,
            "magenta" => isBackground ? Ansi.BgMagenta : Ansi.FgMagenta,
            "cyan" => isBackground ? Ansi.BgCyan : Ansi.FgCyan,
            "white" => isBackground ? Ansi.BgWhite : Ansi.FgWhite,
            _ => isBackground ? Ansi.BgDefault : Ansi.FgDefault
        };
    }
}