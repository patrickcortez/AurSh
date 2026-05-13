namespace AurShell.BlackBoxView;

public static class BypassList
{
    /// <summary>
    /// True for commands that should NOT have a BlackBox around them at all:
    /// fullscreen TUI programs (vim, top, less, htop, tmux, …). They take over
    /// the whole terminal and a wrapping box would just corrupt the display.
    /// </summary>
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

    /// <summary>
    /// True for commands that should run in *passthrough* mode: the BlackBox
    /// header and footer frame the command, but the child inherits the real
    /// terminal stdio so its TTY-dependent behavior (python REPL prompt,
    /// arrow-key history, password prompts, progress bars) works normally.
    ///
    /// Currently used for interactive REPLs on platforms without a usable
    /// pseudo-terminal (Windows, since there's no ConPTY bridge yet). On POSIX
    /// the regular /usr/bin/script PTY wrap is preferred, so this returns false.
    /// </summary>
    public static bool NeedsPassthrough(string commandName, BlackBoxConfig config)
    {
        if (string.IsNullOrWhiteSpace(commandName)) return false;
        string name = System.IO.Path.GetFileNameWithoutExtension(commandName).ToLowerInvariant();

        if (IsBypassed(name, config))
            return false;

        // We use the unconditional NeedsPty list here (not the AURSH_NO_PTY-gated
        // one) so that explicitly disabling PTY-wrap on POSIX still gives the user
        // an in-box framing for known interactive commands.
        if (!IsInteractiveCommand(name)) return false;
        return !PtyHost.IsAvailable();
    }

    private static bool IsInteractiveCommand(string basename)
    {
        // Re-uses PtyHost's known-REPL list via NeedsPty(). If a user sets
        // AURSH_NO_PTY=1 on POSIX, NeedsPty returns false and passthrough
        // doesn't trigger either — they get a regular box (which is the user's
        // explicit choice).
        return PtyHost.NeedsPty(basename);
    }
}
