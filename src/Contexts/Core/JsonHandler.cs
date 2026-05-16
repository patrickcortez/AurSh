// The actual json handler of the Contexts
using System.Reflection.Metadata;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace AurShell.Contexts.Core;



internal static class JsonHandler // Read and Write to json file.
{
    private static readonly string BaseDirectory = AppContext.BaseDirectory; // .aursh

    public static void WriteToJson(Context[] context)
    {
        string jsonfile = Path.Combine(BaseDirectory,"Contexts.json");

        if (!Path.Exists(jsonfile))
        {
            File.Create(jsonfile); // make sure it exists
        }

        JsonSerializerOptions opts = new JsonSerializerOptions
        {
          WriteIndented = true,  
        
        };
        
        StringBuilder sb = new();

        using(FileStream fs = File.OpenWrite(jsonfile))

        JsonSerializer.Serialize(fs,context);
    }

    public static Context[] ReadFromFile() // Our most important function of all
    {
        string jsonpath = Path.Combine(BaseDirectory,"Contexts.json");

        if (!File.Exists(jsonpath))
        {
            File.Create(jsonpath);
        }

        using(FileStream fs = File.OpenRead(jsonpath))

       return JsonSerializer.Deserialize<List<Context>>(fs).ToArray() ?.ToArray() ;
    }


    public static string GetAttribute(string ContextName,string AttributeName)
    {
        string jsonfile = Path.Combine(BaseDirectory,"Contexts.json");
        string target = string.Empty;

        using(FileStream sw = File.OpenRead(jsonfile))
        {
            Context cons = JsonSerializer.Deserialize<Context>(jsonfile);   
            foreach(var con in cons)
            {
                if(con.name == ContextName)
                {
                    target = con.GetValue(AttributeName); // grabs the target value in the target attribute.
                }
            }

        }

        return target;
    }

    
}