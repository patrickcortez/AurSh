using AurShell.Core;
using AurShell.Parser;

namespace AurShell.Commands;

public static class AurshLocalCommand
{
    public static int Execute(SimpleCommandNode cmd, ShellEnvironment env)
    {
        foreach (string arg in cmd.Args)
        {
            int eq = arg.IndexOf('=');
            if (eq > 0)
            {
                string name = arg.Substring(0, eq).Trim();
                string val = arg.Substring(eq + 1).Trim();
                if ((val.StartsWith('"') && val.EndsWith('"')) || (val.StartsWith('\'') && val.EndsWith('\'')))
                    val = val.Substring(1, val.Length - 2);

                env.SetLocal(name, env.Expand(val));
            }
            else
            {
                env.SetLocal(arg, "");
            }
        }
        return 0;
    }
}
