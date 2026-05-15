using AurShell.Utils;
using AurShell.Contexts.Core;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AurShell.Contexts;

internal class ContextEntry
{

    List<string> builtins = new () // builtins commands
    {
      "add","del","list","insert","remove","update"  
    };
    // List<Context> ContextList;
    Action<string,bool> print = (string data,bool isError) =>{

        if(data.Length < 1)
        {
            Console.Error.WriteLine("Data cannot be empty!");
            return;
        }

        if (isError)
        {
            Console.WriteLine($"{Ansi.FgRed}AurSh: {data}");
        }
        else
        {
            Console.WriteLine(data);
        }
    };



    private void DisplayHelp()
    {
        string msg = $@"
        
  {Ansi.FgBrightBlue} Usage:
        {Ansi.FgBlue} aursh-context new <obj-name>=(<atrribute>:<value>,<atrribute>:<value>, ...)
                      aursh-context del <obj-name>
                      aursh-context list
                      aursh-context  insert <obj-name> <attribute> <value>
                      aursh-context  remove <obj-name> <attribute>
                      aursh-context  update <obj-name> <attribute> <value>
        
        ";
        print(msg,false);
    }

    public void Main(params string[] args)
    {

        if(args.Count() < 1)
        {
            DisplayHelp();
            return;
        }


    }
}