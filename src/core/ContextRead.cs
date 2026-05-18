using AurShell.Contexts.Core;

namespace AurShell.Core;

internal class ContextReader
{
    readonly string jsonfile = Path.Combine(AppContext.BaseDirectory,"Contexts.json");

    Context[] cons;

   public ContextReader()
    {
        if (!File.Exists(jsonfile))
        {
            File.Create(jsonfile);
        }

        cons = JsonHandler.ReadFromFile();

    }

    public string GetAttributeValue(string AttributeName)
    {
        foreach(Context con in cons)
        {
            if(con.ContextName == AttributeName)
            {
                return con.GetValue(AttributeName);
            }
        }

        return string.Empty;
    }


}