using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json.Nodes;
using AurShell.Lua;
using AurShell.Parser;
using Attribute = (string key, string value);
namespace AurShell.Contexts.Core;


sealed class Utility
{
    private static char[] endings =
    {
        '{',
        '}',
        '(',
        ')'
    };

    public static Dictionary<string, string> AttributeTokenizer(string data) // {"Attribute":"Value1","Attribute":"Value2"}
    {
        Dictionary<string, string> attrs = new();
        bool inQoutes = false;
        StringBuilder sb = new(), attrname = new(), attrval = new();


        foreach (char c in data)
        {
            if (char.IsWhiteSpace(c) && !inQoutes)
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
                attrs.Add(attrname.ToString(), attrval.ToString());
                attrname.Clear();
                attrval.Clear();
                sb.Clear();
                continue;
            }

            if (c.Equals('"'))
            {
                inQoutes = !inQoutes; // if in qoutes we toggle our boolean
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

        if (sb.Length >= 1)
        {
            attrval = new(sb.ToString());
            attrs.Add(attrname.ToString(), attrval.ToString());
        }

        return attrs;
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
    }; // all done =D

    int ExecuteCommand(string cmd, params string[] args)
    {


        return cmd switch
        {
            "new" => AddContext(args[0]),
            "del" => DeleteContext(args[0]),
            "list" => ListContexts(),
            "update" => UpdateAttribute(args[0], args[1], args[2]),
            "insert" => InsertAttribute(args[0], args[1], args[2]),
            "remove" => RemoveAttribute(args[0], args[1]),
            _ => commandnotfound(cmd)
        };
    }

    private int AddContext(string contextdata) // void to int for exit codes.
    {
        string[] data = contextdata.Split('=', StringSplitOptions.TrimEntries);
        Dictionary<string, string> attrs = new(Utility.AttributeTokenizer(data[1]));
        Parser.Context con = new Parser.Context(data[0], attrs);

        if (attrs.Count() >= 1)
        {
            cons.Add(con);

            Writer.AddContext(con.ContextName, con.GetAttributes());
            return 0;
        }

        return 1;
    }

    private int DeleteContext(string ContextName)
    {
        foreach (Parser.Context con in cons)
        {
            if (con.ContextName == ContextName)
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
        if (cons.Count < 1)
        {
            return 1;
        }

        Console.WriteLine("Contexts:\n---------------");
        foreach (Parser.Context con in cons)
        {
            Console.WriteLine(con.ContextName);
        }

        return 0;
    }

    private int UpdateAttribute(string ContextName, string AttributeName, string NewValue)
    {
        foreach (Parser.Context con in cons)
        {
            if (con.ContextName.Equals(ContextName))
            {
                con.ChangeAttributeValue(AttributeName, NewValue);
                Writer.OverWriteFile(cons.ToArray());
                return 0;
            }
        }

        return 1;
    }

    private int InsertAttribute(string ContextName, string AttributeName, string NewValue)
    {
        foreach (Parser.Context con in cons)
        {
            if (con.ContextName.Equals(ContextName))
            {
                con.InsertAttribute(AttributeName, NewValue);
                Writer.OverWriteFile(cons.ToArray());
                return 0;
            }
        }

        return 1;
    }

    private int RemoveAttribute(string ContextName, string AttributeName)
    {
        foreach (Context con in cons)
        {
            if (con.ContextName.Equals(ContextName))
            {
                int exit = con.RemoveAttribute(AttributeName);


                if (exit == 0)
                {
                    Writer.OverWriteFile(cons.ToArray());
                }

                return exit;
            }
        }

        return 1;
    }



    private int commandnotfound(string cmd)
    {
        Console.Error.WriteLine($"Command: {cmd} ,does not exist!");
        return 1;
    }


    public Builtins(ref int exitcode, params string[] args)
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

        exitcode = ExecuteCommand(cmd, newargs.ToArray());
    }
}