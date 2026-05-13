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

        // Interactive REPLs (python, ssh, mysql, etc.) need a real TTY to behave
        // correctly. On platforms where we cannot allocate one (currently Windows,
        // since there is no ConPTY bridge yet) we must bypass the box and let the
        // child attach to the real terminal directly, otherwise its stdin is a
        // dumb pipe and arrow-key / Escape bytes corrupt its parser.
        if (PtyHost.NeedsPty(name) && !PtyHost.IsAvailable())
            return true;

        return false;
    }
}
