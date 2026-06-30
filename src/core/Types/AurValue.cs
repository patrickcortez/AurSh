using System;
using System.Collections.Generic;
using System.Linq;

namespace AurShell.Core.Types;

public enum AurValueType
{
    String,
    Int,
    Float,
    Bool,
    List,
    Object,
    Null,
    Function
}

public abstract class AurValue
{
    public abstract AurValueType Type { get; }
    
    // Every value must be able to convert to a string for standard POSIX shell commands
    public abstract override string ToString();
    
    public virtual bool IsTruthy()
    {
        return true;
    }

    public virtual AurValue CallMethod(string methodName, List<AurValue> args)
    {
        throw new Exception($"Method '{methodName}' does not exist on type {Type}");
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

    public override AurValue CallMethod(string methodName, List<AurValue> args)
    {
        switch (methodName.ToLowerInvariant())
        {
            case "length":
                return new AurInt(Value.Length);
            case "toupper":
                return new AurString(Value.ToUpperInvariant());
            case "tolower":
                return new AurString(Value.ToLowerInvariant());
            default:
                return base.CallMethod(methodName, args);
        }
    }
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
    public override string ToString() => Value ? "true" : "false";
    public override bool IsTruthy() => Value;
}

public class AurFunction : AurValue
{
    public ICommandNode Node { get; set; }
    public ShellEnvironment Env { get; set; }

    public AurFunction(ICommandNode node, ShellEnvironment env)
    {
        Node = node;
        Env = env;
    }

    public override AurValueType Type => AurValueType.Function;
    public override string ToString() => "[object Function]";
    public override bool IsTruthy() => true;
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

    public override AurValue CallMethod(string methodName, List<AurValue> args)
    {
        switch (methodName.ToLowerInvariant())
        {
            case "length":
                return new AurInt(Values.Count);
            case "push":
                Values.AddRange(args);
                return new AurInt(Values.Count);
            case "join":
                string delimiter = args.Count > 0 ? args[0].ToString() : " ";
                return new AurString(string.Join(delimiter, Values));
            default:
                return base.CallMethod(methodName, args);
        }
    }
    
    public override bool IsTruthy() => Values.Count > 0;
}

public class AurObject : AurValue
{
    public Dictionary<string, AurValue> Properties { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    
    public override AurValueType Type => AurValueType.Object;
    
    public override string ToString()
    {
        try
        {
            if (Properties.Count == 0)
            {
                return "{}";
            }
            
            var props = Properties.Select(kv => $"{kv.Key}: {kv.Value?.ToString() ?? "null"}");
            return $"{{ {string.Join(", ", props)} }}";
        }
        catch (Exception ex)
        {
            return $"[object Object - Error: {ex.Message}]";
        }
    }
    
    public override bool IsTruthy() => true;

    public override AurValue CallMethod(string methodName, List<AurValue> args)
    {
        if (Properties.TryGetValue(methodName, out var value) && value is AurFunction func)
        {
            var executor = new Executor(func.Env, System.Environment.CurrentDirectory);
            var evaluator = new AstEvaluator(func.Env, executor, System.Environment.CurrentDirectory);

            var fnNode = func.Node as FunctionNode;
            if (fnNode != null)
            {
                // Push positional args
                var strArgs = new List<string>();
                foreach(var arg in args) strArgs.Add(arg.ToString());
                
                func.Env.PushPositionalArguments(strArgs);
                evaluator.Visit(fnNode.Body);
                func.Env.PopPositionalArguments();
                
                return func.Env.LastReturnValue ?? new AurInt(func.Env.LastExitCode);
            }
        }
        return base.CallMethod(methodName, args);
    }
}
