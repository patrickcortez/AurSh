using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json.Nodes;
using AurShell.Parser;
using Attribute = (string key ,string value);
namespace AurShell.Contexts.Core;


sealed class Utility
{
    private static char[] endings =
    {
        '{',
        '}'
    };

   public static Attribute[] AttributeTokenizer(string data) // {"Attribute":"Value1","Attribute":"Value2"}
    {
        List<Attribute> attrs = new();
        bool inQoutes = false;
        StringBuilder sb = new(),attrname = new(), attrval = new();
        

        foreach (char c in data)
        {
            if (char.IsWhiteSpace(c))
            {
                continue;
            }

            if (endings.Contains(c))
            {
                continue;
            }

            if (c.Equals(',') && !inQoutes)
            {
                // Next Attribute
                attrval.Append(sb);
                attrs.Add(new Attribute(attrname.ToString(),attrval.ToString()));
                attrname.Clear();
                attrval.Clear();
                sb.Clear();
                continue;
            }

            if (c.Equals('"'))
            {
                inQoutes=!inQoutes; // if in qoutes we toggle our boolean
                continue;
            }

            if (c.Equals(':') && !inQoutes)
            {

                attrname.Append(sb);
                sb.Clear();
                continue;
            }

            sb.Append(c);
            

        }

        if(sb.Length >= 1)
        {
            attrval = new(sb.ToString());
            attrs.Add(new Attribute(attrname.ToString(),attrval.ToString()));
        }

        return attrs.ToArray();
    }
}

internal class Builtins
{
    Reader read;

    string Command;
    List<Parser.Context>? cons;
    List<string> cmds = new()
    {
        "new","del","list","update", "insert","remove" // new, del and list: Done!
    }; // 3 more to go -_-

    int ExecuteCommand(string cmd,params string[] args)
    {


        return cmd switch
        {
            "new" => AddContext(args[1]),
            "del" => DeleteContext(args[1]),
            "list" => ListContexts(),
             _ => commandnotfound(cmd)
        };
    }

    private int AddContext(string contextdata) // void to int for exit codes.
    {
        string[] data = contextdata.Split('=',StringSplitOptions.TrimEntries);
        Attribute[] attrs = Utility.AttributeTokenizer(data[1]);
        Parser.Context con = new Parser.Context(data[0],attrs.ToDictionary());

        if(attrs.Count() >= 1){
            cons.Add(con);

            Writer.AddContext(con.ContextName,con.GetAttributes());
            return 0;
        }

        return 1;
    }

    private int DeleteContext(string ContextName)
    {
        foreach(Parser.Context con in cons)
        {
            if(con.ContextName == ContextName)
            {
                int index = cons.IndexOf(con);

                if (cons.Remove(con))
                {
                    Writer.OverWriteFile(cons.ToArray());
                    return 0;
                }

            }
        }

        return 1;
    }

    private int ListContexts()
    {
        if(cons.Count < 1)
        {
            return 1;
        }

        Console.WriteLine("Contexts:\n---------------");
        foreach(Parser.Context con in cons)
        {
            Console.WriteLine(con.ContextName);
        }

        return 0;
    }

    private int commandnotfound(string cmd)
    {
        Console.Error.WriteLine($"Command: {cmd} ,does not exist!");
        return 1;
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
        read = new();
        cons = read.GetContexts().ToList();
        List<string> newargs = new(args.Skip(1));
        
        ExecuteCommand(cmd,newargs.ToArray());
    }
}