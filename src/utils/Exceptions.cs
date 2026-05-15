using System.Collections;
using Microsoft.VisualBasic;

namespace AurShell.Utils;

internal class NUllAttributeException(string msg = "Attribute does not exist.")
: Exception(msg)
{
    public string Message()
    {
        return "Attribute does not exist";
    }

    public string GetStackTrace()
    {
        return Environment.StackTrace;
    }

    public int ErrorCode = 6969; // yep this is my error code }=}}
}