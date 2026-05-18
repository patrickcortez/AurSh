using System.Security.Cryptography.X509Certificates;
using System.Text;
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

    public static bool isContext(string data)
    {
        Reader read = new Reader();
        Context[]? cons = read.GetContexts();
        StringBuilder sb = new();

        foreach(char c in data)
        {
            if (char.IsWhiteSpace(c))
            {
                continue;
            }

            if (c.Equals(':'))
            {
                break;
            }

            sb.Append(c);
        }

        foreach(Context con in cons)
        {
            if (con.ContextName.Equals(sb.ToString()))
            {
                return true;
            }
        }

        return false;

    }


}