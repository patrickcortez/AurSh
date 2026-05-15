// this file is mainly for Definitions of a context and its attributes
using System.Collections;
using System.Dynamic;
using System.Numerics;
using System.Runtime.CompilerServices;
using AurShell.Utils;
using Attribute = (string key,string value);

namespace AurShell.Contexts.Core;

/*  
    Context Data structure:
    {
        "context-name":{
            "Attribute1":"Value",
            "Attribute2":"Value2",
            "OPtion1":false,
            "Amount1":10
        }


    }
*/



internal record Context(string name,List<Attribute> attr)
    : IEnumerable<Context>
{
    List<Attribute> attrs = attr;
    public string ContextName {get;} = name;

    public IEnumerator<Context> GetEnumerator()
    {
        yield return this;
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }

    public void AddAttribute(Attribute attri) // for later
    {
        attrs.Add(attri);
    }

    public void ModAttribute(string key,string data)
    {
        int index = 0;
        foreach(Attribute k in attrs)
        {
            if(k.key == key)
              index = attrs.IndexOf(k);
        }

        attrs[index] = new Attribute(key,data);
    }
    public string GetValue(string key)
    {
        try{
        string val = string.Empty;

        foreach(Attribute att in attr){

            if(att.key == key)
            {
                val = att.value;
                break;
            }
        }


        return val;
        }catch(NUllAttributeException ex)
        {
            Console.Error.Write($"AurSh-Context: {ex.Message()} , {ex.ErrorCode}");
            throw;
        }
    }
}