using AurShell.Contexts.Core;
using AurShell.Parser;
using AurShell.Utils;

namespace AurShell.Core;

internal class ContextReader
{
    readonly string confile = Path.Combine(Platform.HomeDirectory,".aursh","Contexts.con");

    Context[]? cons;

   public ContextReader()
    {
        if (!File.Exists(confile))
        {
            File.Create(confile);
        }

        Reader read = new Reader();

        cons = read.GetContexts();

    }

    public string GetAttributeValue(string ContextName,string AttributeName)
    {
        foreach(Context con in cons)
        {
            if(con.ContextName == ContextName)
            {
                return con.GetAttributeValue(AttributeName);
            }
        }

        return string.Empty;
    }


}