namespace AurShell.Parser;

using System.Text;
using AurShell.Utils;

internal struct Context
{
    public string ContextName {get;set;}

    private Dictionary<string,string> Attributes;

    public Context(string NewContextName,Dictionary<string,string> NewAttributes)
    {
        if (!string.IsNullOrWhiteSpace(NewContextName))
        {
            ContextName = new(NewContextName);
            Attributes = new(NewAttributes);
        }

        
    }

    //Context Operations
    public string GetAttributeValue(string AttributeName)
    {
        if (Attributes.ContainsKey(AttributeName))
        {
            return Attributes[AttributeName];
        }

        return string.Empty;
    }

    public int ChangeAttributeValue(string AttributeName,string NewValue)
    {
        if (!Attributes.ContainsKey(AttributeName)) //Guard clauses
        {
            return 1;
        }

        if (string.IsNullOrWhiteSpace(NewValue))
        {
            return 1;
        }

        Attributes[AttributeName] = NewValue;
        return 0;
    }

    public int InsertAttribute(string AttributeName,string AttributeValue)
    {
        if(string.IsNullOrWhiteSpace(AttributeName) || string.IsNullOrWhiteSpace(AttributeValue))
        {
            return 1;
        }

        Attributes.Add(AttributeName,AttributeValue);
        return 0;
    }

    public int RemoveAttribute(string AttributeName)
    {
        if (Attributes.Remove(AttributeName))
        {
            return 0;
        }
        
        return 1;
    }

    public Dictionary<string,string> GetAttributes()
    {
        return Attributes;
    }
}

internal static class Helper
{
    public static string configfile = Path.Combine(Platform.HomeDirectory,".aursh","Contexts.con");

    public static void EnsureConfigExists()
    {
        if (File.Exists(configfile))
        {
            return;
        }

        File.Create(configfile);
        return;
    }

    public static bool isAttribute(string data)
    {
        bool hasSemicolon = false;
        StringBuilder AttributeName = new(),AttributeValue = new();

        foreach(char c in data)
        {
            if (char.IsWhiteSpace(c))
            {
                continue;
            }

            if(c.Equals(':')){

                hasSemicolon = true;
            }

            if (hasSemicolon)
            {
                AttributeValue.Append(c);
                continue;
            }

            AttributeName.Append(c);


        }

        if(AttributeName.Length >= 1 && AttributeValue.Length >= 1)
        {
            return true;
        }

        return false;

    }

    public static KeyValuePair<string,string> TokenizeAttribute(string line)
    {
        bool inQoutes = true,isFirst = true;
        StringBuilder Name = new(),Value = new();
        foreach(char c in line)
        {
            if (char.IsWhiteSpace(c) && !inQoutes)
            {
                continue;
            }

            if (c.Equals('"'))
            {
                inQoutes = !inQoutes;
                continue;
            }

            if (c.Equals(':') && !inQoutes)
            {
                isFirst = false;
                continue;
            }

            if (!isFirst)
            {
                Value.Append(c);
                continue;
            }

            Name.Append(c);

        }   

        return new(Name.ToString(),Value.ToString());
    }

}