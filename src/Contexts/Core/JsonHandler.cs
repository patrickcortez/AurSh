using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AurShell.Contexts.Core;



internal static class JsonHandler // Read and Write to json file.
{
    private static readonly string BaseDirectory = AppContext.BaseDirectory; // .aursh

    public static void WriteToJson(Context[] contexts)
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
        
        StringBuilder sb = new StringBuilder();

        using(StreamWriter sw = new StreamWriter(jsonfile)){
            
            foreach(Context con in contexts)
            {
                string? json = JsonSerializer.Serialize(con,opts);

                sw.WriteLine(json);
            }
        }

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
                    target = con.GetValue(AttributeName);
                }
            }

        }

        return target;
    }

    
}