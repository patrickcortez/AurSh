using System;
using System.Collections.Generic;

namespace AurShell.Core.Types;

public enum AurValueType
{
    String,
    Int,
    Float,
    Bool,
    List,
    Object,
    Null
}

public abstract class AurValue
{
    public abstract AurValueType Type { get; }
    
    // Every value must be able to convert to a string for standard POSIX shell commands
    public abstract override string ToString();
    
    // Utility for truthiness (like in Bash or JS)
    public virtual bool IsTruthy()
    {
        return true;
    }
}

public class AurNull : AurValue
{
    public override AurValueType Type => AurValueType.Null;
    public override string ToString() => "";
    public override bool IsTruthy() => false;
}

public class AurString : AurValue
{
    public string Value { get; set; }
    
    public AurString(string value)
    {
        Value = value ?? "";
    }
    
    public override AurValueType Type => AurValueType.String;
    public override string ToString() => Value;
    public override bool IsTruthy() => !string.IsNullOrEmpty(Value);
}

public class AurInt : AurValue
{
    public long Value { get; set; }
    
    public AurInt(long value)
    {
        Value = value;
    }
    
    public override AurValueType Type => AurValueType.Int;
    public override string ToString() => Value.ToString();
    public override bool IsTruthy() => Value != 0;
}

public class AurFloat : AurValue
{
    public double Value { get; set; }
    
    public AurFloat(double value)
    {
        Value = value;
    }
    
    public override AurValueType Type => AurValueType.Float;
    public override string ToString() => Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
    public override bool IsTruthy() => Value != 0.0;
}

public class AurBool : AurValue
{
    public bool Value { get; set; }
    
    public AurBool(bool value)
    {
        Value = value;
    }
    
    public override AurValueType Type => AurValueType.Bool;
    public override string ToString() => Value ? "true" : "false"; // or "1"/"0"? We'll use JS style for now.
    public override bool IsTruthy() => Value;
}

public class AurList : AurValue
{
    public List<AurValue> Values { get; set; } = new();
    
    public override AurValueType Type => AurValueType.List;
    
    // In POSIX shells, lists usually expand space-separated
    public override string ToString()
    {
        return string.Join(" ", Values);
    }
    
    public override bool IsTruthy() => Values.Count > 0;
}

public class AurObject : AurValue
{
    public Dictionary<string, AurValue> Properties { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    
    public override AurValueType Type => AurValueType.Object;
    
    public override string ToString()
    {
        return "[object Object]";
    }
    
    public override bool IsTruthy() => true;
}
