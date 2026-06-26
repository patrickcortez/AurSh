using System;
using System.Linq;
using AurShell.Core;
using AurShell.Parser;

namespace AurShell.Commands;

public static class AurshAssocCommand
{
    public static int Execute(SimpleCommandNode cmd, ShellEnvironment env)
    {
        if (cmd.Args.Count == 0)
        {
            var all = env.Associator.GetAll();
            if (all.Count == 0)
            {
                Console.WriteLine("No file associations set.");
                return 0;
            }

            foreach (var kv in all)
            {
                Console.WriteLine($"aursh-assoc {kv.Key} \"{kv.Value}\"");
            }
            return 0;
        }

        string ext = cmd.Args[0];

        if (cmd.Args.Count == 1)
        {
            string? template = env.Associator.GetAssociation(ext);
            if (template != null)
            {
                Console.WriteLine($"aursh-assoc {ext} \"{template}\"");
                return 0;
            }
            else
            {
                Console.Error.WriteLine($"aursh: aursh-assoc: no association found for extension {ext}");
                return 1;
            }
        }

        if (cmd.Args.Count >= 2)
        {
            if (cmd.Args[1] == "--remove" || cmd.Args[1] == "--delete" || cmd.Args[1] == "--rm")
            {
                if (env.Associator.RemoveAssociation(ext))
                {
                    Console.WriteLine($"Removed association for {ext}");
                    return 0;
                }
                else
                {
                    Console.Error.WriteLine($"aursh: aursh-assoc: no association found for extension {ext}");
                    return 1;
                }
            }

            string template = string.Join(" ", cmd.Args.Skip(1));
            if ((template.StartsWith('"') && template.EndsWith('"')) ||
                (template.StartsWith('\'') && template.EndsWith('\'')))
            {
                template = template.Substring(1, template.Length - 2);
            }

            env.Associator.SetAssociation(ext, template);
            return 0;
        }

        return 1;
    }
}
