namespace AurShell.Core;

internal class ContextReader
{
    readonly string jsonfile = Path.Combine(AppContext.BaseDirectory,"Contexts.json");
   public ContextReader()
    {
        if (!File.Exists(jsonfile))
        {
            File.Create(jsonfile);
        }


    }
}