using System.Text;

namespace AurShell.Parser; // Context Parser of AurSh

/* Simple structure:

[Context]
Attribute=Value # could be string or int
Attribute2=Value

[Context2]
Attribute3=Value

*/


public class Reader //instance based.
{
    private List<string> LineBuffer; // store all lines in file in here.
    private string configfile = Helper.configfile;
    List<Context> contexts ;

    public Reader()
    {
        Helper.EnsureConfigExists(); // Always make sure the config file exists
        LineBuffer = new();
        contexts = new();
        ReadLines();
    }

    private void ReadLines()
    {
        using( StreamReader sr = new StreamReader(configfile)){
            string? line;
            while((line = sr.ReadLine()) != null)
            {
                LineBuffer.Add(line);
            }
        }

        InterpretLines();
    }

    private void InterpretLines()
    {
        StringBuilder Current_ContextName = new();
        Dictionary<string,string> Attributes = new();
        foreach(string line in LineBuffer)
        {
            if (string.IsNullOrWhiteSpace(line)) // for every whitespace if the current contextname length isnt 0 and attribute coun isnt zero, we append a new context in our contexts.
            {
                if(Current_ContextName.Length > 0 && Attributes.Count > 0) // Context Validation
                {
                    contexts.Add(new Context(Current_ContextName.ToString(),new Dictionary<string, string>(Attributes)));
                    Current_ContextName.Clear();
                    Attributes.Clear();
                }
                continue;
            }

            if(line.StartsWith('[') && line.EndsWith(']')) // Context Name
            {
                Current_ContextName.Append(line.TrimStart('[').TrimEnd(']').Trim());
                continue;
            }

            if (Helper.isAttribute(line))
            {
                var kvp = Helper.TokenizeAttribute(line);
                if (!string.IsNullOrEmpty(kvp.Key))
                {
                    Attributes[kvp.Key] = kvp.Value;
                }
            }
        }

        if(Current_ContextName.Length > 0 && Attributes.Count > 0)
        {
            contexts.Add(new Context(Current_ContextName.ToString(),new Dictionary<string, string>(Attributes)));
            Current_ContextName.Clear();
            Attributes.Clear();
        }
    }
    public string GetAttribute(string ContextName,string AttributeName) // Context={Attribute:Value}
    {
        string value = string.Empty;
        foreach(Context con in contexts)
        {
            if(con.ContextName == ContextName)
            {
                value = con.GetAttributeValue(AttributeName);
                break;
            }

        }

        return value;
    }

    public Context[]? GetContexts()
    {
        return contexts.ToArray() ?.ToArray();
    }
}