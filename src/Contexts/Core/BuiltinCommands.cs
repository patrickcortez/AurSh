using System.Runtime.CompilerServices;
using Attribute = (string key ,string value);
namespace AurShell.Contexts.Core;


internal class Builtins
{
    string Command;
    List<Context>? cons;
    List<string> cmds = new()
    {
        "new","del","list","update", "insert","remove"
    };

    Action ExecuteCommand(string cmd,params string[] args)
    {

        // TOKENIZE THE FUCKING ATTRIBUTES

        return cmd switch
        {
            "add" => Add(args[0],),
             _ => commandnotfound(cmd)
        };
    }

    private void AddContext(string contextname,Attribute[] attrs)
    {
        Context con = new(contextname,attrs.ToList<Attribute>());
        cons.Add(con);
    }

    private void commandnotfound(string cmd)
    {
        Console.Error.WriteLine($"Command: {cmd} ,does not exist!");
    }
    

    public Builtins(params string[] args)
    {
        string cmd = args[0];
        if (string.IsNullOrEmpty(cmd))
        {
            Console.Error.WriteLine("Command cannot be empty!");
            return;
        }

        Command = cmd;
        cons = JsonHandler.ReadFromFile().ToList<Context>();
        List<string> newargs = new(args.Skip(1));
        
        ExecuteCommand(cmd,newargs.ToArray());
    }
}