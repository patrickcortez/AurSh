namespace AurShell.Parser;

using System.Text;
using AurShell.Utils;

public struct Context
{
    public string ContextName { get; set; }

    private Dictionary<string, string> Attributes;

    public Context(string NewContextName, Dictionary<string, string> NewAttributes)
    {
        // Always initialize — NativeAOT's trimmer will scream if these are left hanging
        ContextName = string.Empty;
        Attributes = new Dictionary<string, string>();

        if (!string.IsNullOrWhiteSpace(NewContextName))
        {
            ContextName = new(NewContextName);
            Attributes = new(NewAttributes);
        }
    }

    //Context Operations
    public string GetAttributeValue(string AttributeName)
    {
        if (Attributes.ContainsKey(AttributeName))
        {
            return Attributes[AttributeName];
        }

        return string.Empty;
    }

    public int ChangeAttributeValue(string AttributeName, string NewValue)
    {
        if (!Attributes.ContainsKey(AttributeName)) //Guard clauses
        {
            return 1;
        }

        if (string.IsNullOrWhiteSpace(NewValue))
        {
            return 1;
        }

        Attributes[AttributeName] = NewValue;
        return 0;
    }

    public int InsertAttribute(string AttributeName, string AttributeValue)
    {
        if (string.IsNullOrWhiteSpace(AttributeName) || string.IsNullOrWhiteSpace(AttributeValue))
        {
            return 1;
        }

        Attributes.Add(AttributeName, AttributeValue);
        return 0;
    }

    public int RemoveAttribute(string AttributeName)
    {
        if (Attributes.Remove(AttributeName))
        {
            return 0;
        }

        return 1;
    }

    public Dictionary<string, string> GetAttributes()
    {
        return Attributes;
    }
}

internal static class Helper
{
    public static string configfile = Path.Combine(Platform.HomeDirectory, ".aursh", "AurSh.config.con");

    public static void EnsureConfigExists()
    {
        if (File.Exists(configfile))
        {
            return;
        }

        string? dir = Path.GetDirectoryName(configfile);
        if (dir != null && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        File.WriteAllText(configfile, "[Config]\nPromptSpacing=Compressed\nPromptLine=none\nSegmentEdges=angled\nVerbose=true\n");
    }

    public static int FindDelimiterIndex(string line)
    {
        bool inQuotes = false;
        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (c == '"')
            {
                inQuotes = !inQuotes;
            }
            else if ((c == '=' || c == ':') && !inQuotes)
            {
                return i;
            }
        }
        return -1;
    }

    public static bool isAttribute(string data)
    {
        if (string.IsNullOrWhiteSpace(data)) return false;
        string trimmed = data.Trim();
        if (trimmed.StartsWith("[") && trimmed.EndsWith("]")) return false;
        return FindDelimiterIndex(trimmed) != -1;
    }

    public static KeyValuePair<string, string> TokenizeAttribute(string line)
    {
        string trimmed = line.Trim();
        int delimIdx = FindDelimiterIndex(trimmed);
        if (delimIdx == -1)
        {
            return new KeyValuePair<string, string>(string.Empty, string.Empty);
        }

        string keyPart = trimmed.Substring(0, delimIdx).Trim();
        string valuePart = trimmed.Substring(delimIdx + 1).Trim();

        if (keyPart.StartsWith("\"") && keyPart.EndsWith("\"") && keyPart.Length >= 2)
        {
            keyPart = keyPart.Substring(1, keyPart.Length - 2);
        }
        if (valuePart.StartsWith("\"") && valuePart.EndsWith("\"") && valuePart.Length >= 2)
        {
            valuePart = valuePart.Substring(1, valuePart.Length - 2);
        }

        return new KeyValuePair<string, string>(keyPart, valuePart);
    }

}