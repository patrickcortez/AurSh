using AurShell.Utils;

namespace AurShell.Parser;

public static class Writer
{
    static string configfile = Path.Combine(Platform.HomeDirectory, ".aursh", "Contexts.con");

    public static void Setup() //small helper method to ensure our confiug file exists
    {
        Helper.EnsureConfigExists();
    }

    public static void AddContext(string ContextName, Dictionary<string, string> Attributes)
    {
        using (StreamWriter sw = new(configfile, true))
        {

            sw.WriteLine($"[{ContextName}]");

            foreach (KeyValuePair<string, string> attribute in Attributes)
            {
                sw.WriteLine($"\"{attribute.Key}\"=\"{attribute.Value}\"");
            }

            sw.WriteLine();
        }
    }

    public static void OverWriteFile(Context[] contexts)
    {
        Dictionary<string, string> attr;
        using (StreamWriter sw = new(configfile, false))
        {
            foreach (Context con in contexts)
            {
                sw.WriteLine($"[{con.ContextName}]");

                attr = new(con.GetAttributes());

                foreach (KeyValuePair<string, string> att in attr)
                {
                    sw.WriteLine($"\"{att.Key}\"=\"{att.Value}\"");
                }

                sw.WriteLine();
            }
        }
    }


}