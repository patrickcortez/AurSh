using AurShell.Parser;
using AurShell.Utils;

namespace AurShell.Core;

internal class ContextReader
{
    readonly string confile = Path.Combine(Platform.HomeDirectory, ".aursh", "Contexts.con");

    Context[]? cons;

    public ContextReader()
    {
        if (!File.Exists(confile))
        {
            File.Create(confile).Dispose();
        }

        Reader read = new Reader();

        cons = read.GetContexts();

    }

    public string GetAttributeValue(string ContextName, string AttributeName)
    {
        foreach (Context con in cons)
        {
            if (con.ContextName == ContextName)
            {
                return con.GetAttributeValue(AttributeName);
            }
        }

        return string.Empty;
    }

    public static bool isContext(string data)
    {
        if (string.IsNullOrEmpty(data)) return false;
        int colonIdx = data.IndexOf(':');
        if (colonIdx <= 0) return false;

        string contextCandidate = data.Substring(0, colonIdx).Trim();

        Reader read = new Reader();
        Context[]? cons = read.GetContexts();
        if (cons == null) return false;

        foreach (Context con in cons)
        {
            if (con.ContextName.Equals(contextCandidate))
            {
                return true;
            }
        }

        return false;
    }


}