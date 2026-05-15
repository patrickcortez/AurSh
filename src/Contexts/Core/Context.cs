using System.Collections;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace AurShell.Contexts.Core;

record Attribute(string key,string value);

internal record Context(string name, Attribute[] attr)
    : IEnumerable<Context>
{
    public IEnumerator<Context> GetEnumerator()
    {
        yield return this;
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }

    public IEnumerable GetAttributes()
    {
        return attr;
    }
    
    public string GetValue(string key)
    {
        string val = string.Empty;

        foreach(Attribute att in attr){

            if(att.key == key)
            {
                val = att.value;
                break;
            }
        }

        return val;
    }
}