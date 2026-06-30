using System.Collections.Generic;

namespace AurShell.Core.AST;

public interface ExpressionNode
{
}

public class LiteralExpressionNode : ExpressionNode
{
    public Types.AurValue Value { get; set; }

    public LiteralExpressionNode(Types.AurValue value)
    {
        Value = value;
    }
}

public class IdentifierExpressionNode : ExpressionNode
{
    public string Name { get; set; }

    public IdentifierExpressionNode(string name)
    {
        Name = name;
    }
}

public enum BinaryOperator
{
    Add, Subtract, Multiply, Divide,
    Equal, NotEqual, LessThan, GreaterThan, LessThanOrEqual, GreaterThanOrEqual,
    LogicalAnd, LogicalOr
}

public class BinaryExpressionNode : ExpressionNode
{
    public ExpressionNode Left { get; set; }
    public BinaryOperator Operator { get; set; }
    public ExpressionNode Right { get; set; }

    public BinaryExpressionNode(ExpressionNode left, BinaryOperator op, ExpressionNode right)
    {
        Left = left;
        Operator = op;
        Right = right;
    }
}

public enum UnaryOperator
{
    Not, Negate
}

public class UnaryExpressionNode : ExpressionNode
{
    public UnaryOperator Operator { get; set; }
    public ExpressionNode Operand { get; set; }

    public UnaryExpressionNode(UnaryOperator op, ExpressionNode operand)
    {
        Operator = op;
        Operand = operand;
    }
}

public class CallExpressionNode : ExpressionNode
{
    public ExpressionNode Callee { get; set; }
    public List<ExpressionNode> Arguments { get; set; }

    public CallExpressionNode(ExpressionNode callee, List<ExpressionNode> arguments)
    {
        Callee = callee;
        Arguments = arguments;
    }
}

public class MemberExpressionNode : ExpressionNode
{
    public ExpressionNode Object { get; set; }
    public string PropertyName { get; set; }

    public MemberExpressionNode(ExpressionNode obj, string propertyName)
    {
        Object = obj;
        PropertyName = propertyName;
    }
}

public class IndexExpressionNode : ExpressionNode
{
    public ExpressionNode Object { get; set; }
    public ExpressionNode Index { get; set; }

    public IndexExpressionNode(ExpressionNode obj, ExpressionNode index)
    {
        Object = obj;
        Index = index;
    }
}

public class AssignmentExpressionNode : ExpressionNode
{
    public ExpressionNode Left { get; set; }
    public ExpressionNode Right { get; set; }

    public AssignmentExpressionNode(ExpressionNode left, ExpressionNode right)
    {
        Left = left;
        Right = right;
    }
}

public class ArrayExpressionNode : ExpressionNode
{
    public List<ExpressionNode> Elements { get; set; }

    public ArrayExpressionNode(List<ExpressionNode> elements)
    {
        Elements = elements;
    }
}

public class ObjectExpressionNode : ExpressionNode
{
    public Dictionary<string, ExpressionNode> Properties { get; set; }

    public ObjectExpressionNode(Dictionary<string, ExpressionNode> properties)
    {
        Properties = properties;
    }
}

public class ExpressionStatementNode : ICommandNode
{
    public ExpressionNode Expression { get; }
    public int Line { get; set; }
    public int Column { get; set; }
    public List<Redirection> Redirections { get; set; } = new List<Redirection>();

    public ExpressionStatementNode(ExpressionNode expression)
    {
        Expression = expression;
    }

    public override string ToString() => Expression.ToString();
}
