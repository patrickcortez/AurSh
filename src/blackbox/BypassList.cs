namespace AurShell.BlackBoxView;

public static class BypassList
{
    public static bool IsBypassed(string commandName, BlackBoxConfig config)
    {
        if (string.IsNullOrWhiteSpace(commandName)) return false;
        string name = System.IO.Path.GetFileNameWithoutExtension(commandName).ToLowerInvariant();

        foreach (string b in config.Bypass)
        {
            if (string.Equals(b.Trim(), name, System.StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }
}
