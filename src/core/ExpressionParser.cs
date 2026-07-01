using System;
using System.Collections.Generic;

namespace AurShell.Core.AST;

public enum Precedence
{
    Lowest = 1,
    Assignment = 2,
    LogicalOr = 3,
    LogicalAnd = 4,
    Equality = 5,
    Relational = 6,
    Additive = 7,
    Multiplicative = 8,
    Prefix = 9,
    Call = 10,
    Index = 11
}

public class ExpressionParser
{
    private readonly List<Token> _tokens;
    private int _pos;

    private readonly Dictionary<TokenType, Func<Token, ExpressionNode>> _prefixParseFns;
    private readonly Dictionary<TokenType, Func<ExpressionNode, Token, ExpressionNode>> _infixParseFns;
    private readonly Dictionary<TokenType, Precedence> _precedences;

    public ExpressionParser(List<Token> tokens, int startPos)
    {
        _tokens = tokens;
        _pos = startPos;
        _prefixParseFns = new Dictionary<TokenType, Func<Token, ExpressionNode>>();
        _infixParseFns = new Dictionary<TokenType, Func<ExpressionNode, Token, ExpressionNode>>();
        _precedences = new Dictionary<TokenType, Precedence>();

        RegisterPrefix(TokenType.Word, ParseIdentifierOrLiteral);
        RegisterPrefix(TokenType.Minus, ParsePrefixExpression);
        RegisterPrefix(TokenType.Not, ParsePrefixExpression);
        RegisterPrefix(TokenType.LeftParen, ParseGroupedExpression);
        RegisterPrefix(TokenType.LeftBracket, ParseArrayLiteral);
        RegisterPrefix(TokenType.LeftBrace, ParseObjectLiteral);

        RegisterInfix(TokenType.Plus, ParseBinaryExpression, Precedence.Additive);
        RegisterInfix(TokenType.Minus, ParseBinaryExpression, Precedence.Additive);
        RegisterInfix(TokenType.Multiply, ParseBinaryExpression, Precedence.Multiplicative);
        RegisterInfix(TokenType.Divide, ParseBinaryExpression, Precedence.Multiplicative);
        RegisterInfix(TokenType.Equal, ParseBinaryExpression, Precedence.Equality);
        RegisterInfix(TokenType.NotEqual, ParseBinaryExpression, Precedence.Equality);
        RegisterInfix(TokenType.LessThan, ParseBinaryExpression, Precedence.Relational);
        RegisterInfix(TokenType.GreaterThan, ParseBinaryExpression, Precedence.Relational);
        RegisterInfix(TokenType.LessThanOrEqual, ParseBinaryExpression, Precedence.Relational);
        RegisterInfix(TokenType.GreaterThanOrEqual, ParseBinaryExpression, Precedence.Relational);
        RegisterInfix(TokenType.And, ParseBinaryExpression, Precedence.LogicalAnd);
        RegisterInfix(TokenType.Or, ParseBinaryExpression, Precedence.LogicalOr);

        RegisterInfix(TokenType.Assign, ParseAssignmentExpression, Precedence.Assignment);
        RegisterInfix(TokenType.LeftParen, ParseCallExpression, Precedence.Call);
        RegisterInfix(TokenType.LeftBracket, ParseIndexExpression, Precedence.Index);
        RegisterInfix(TokenType.Dot, ParseMemberExpression, Precedence.Index);
    }

    public int GetCurrentPosition() => _pos;
    
    // We update position externally if needed, but usually the parser advances it
    public void SetPosition(int pos) => _pos = pos;

    private void RegisterPrefix(TokenType type, Func<Token, ExpressionNode> fn)
    {
        _prefixParseFns[type] = fn;
    }

    private void RegisterInfix(TokenType type, Func<ExpressionNode, Token, ExpressionNode> fn, Precedence precedence)
    {
        _infixParseFns[type] = fn;
        _precedences[type] = precedence;
    }

    private Precedence CurrentPrecedence()
    {
        if (_pos < _tokens.Count && _precedences.TryGetValue(_tokens[_pos].Type, out var p))
            return p;
        return Precedence.Lowest;
    }

    private Token Advance()
    {
        var token = _tokens[_pos];
        _pos++;
        return token;
    }

    private Token Peek()
    {
        if (_pos >= _tokens.Count) return _tokens[_tokens.Count - 1]; // Return EOF or last token
        return _tokens[_pos];
    }

    private bool Match(TokenType type)
    {
        if (Peek().Type == type)
        {
            Advance();
            return true;
        }
        return false;
    }

    public ExpressionNode? ParseExpression(Precedence precedence = Precedence.Lowest)
    {
        if (_pos >= _tokens.Count) return null;

        var token = Advance();
        if (!_prefixParseFns.TryGetValue(token.Type, out var prefixFn))
        {
            // If we don't have a prefix fn, it's not a valid expression start.
            // Rewind and return null so the caller can fall back to ShellCommand
            _pos--;
            return null;
        }

        var leftExp = prefixFn(token);

        while (_pos < _tokens.Count && precedence < CurrentPrecedence())
        {
            var infixToken = Peek();
            if (!_infixParseFns.TryGetValue(infixToken.Type, out var infixFn))
            {
                return leftExp;
            }

            Advance();
            leftExp = infixFn(leftExp, infixToken);
        }

        return leftExp;
    }

    private ExpressionNode ParseIdentifierOrLiteral(Token token)
    {
        if (token.WasQuoted || token.WasSingleQuoted)
        {
            return new LiteralExpressionNode(new Types.AurString(token.Value));
        }

        if (int.TryParse(token.Value, out int iVal))
            return new LiteralExpressionNode(new Types.AurInt(iVal));
        
        if (double.TryParse(token.Value, out double dVal))
            return new LiteralExpressionNode(new Types.AurString(dVal.ToString()));

        if (bool.TryParse(token.Value, out bool bVal))
            return new LiteralExpressionNode(new Types.AurString(bVal ? "true" : "false"));

        if (token.Value == "null")
            return new LiteralExpressionNode(new Types.AurString(""));

        return new IdentifierExpressionNode(token.Value);
    }

    private ExpressionNode ParsePrefixExpression(Token token)
    {
        var right = ParseExpression(Precedence.Prefix);
        if (right == null) throw new Exception($"Expected expression after {token.Value}");
        var op = token.Value == "!" ? UnaryOperator.Not : UnaryOperator.Negate;
        return new UnaryExpressionNode(op, right);
    }

    private ExpressionNode ParseGroupedExpression(Token token)
    {
        var exp = ParseExpression(Precedence.Lowest);
        if (!Match(TokenType.RightParen))
            throw new Exception("Expected ')'");
        return exp!;
    }

    private void SkipNewlines()
    {
        while (_pos < _tokens.Count && _tokens[_pos].Type == TokenType.Newline)
        {
            _pos++;
        }
    }

    private ExpressionNode ParseArrayLiteral(Token token)
    {
        var elements = new List<ExpressionNode>();
        SkipNewlines();
        if (Peek().Type != TokenType.RightBracket)
        {
            do
            {
                SkipNewlines();
                var el = ParseExpression(Precedence.Lowest);
                if (el != null) elements.Add(el);
                SkipNewlines();
            } while (Match(TokenType.Comma));
        }

        if (!Match(TokenType.RightBracket))
            throw new Exception("Expected ']'");

        return new ArrayExpressionNode(elements);
    }

    private ExpressionNode ParseObjectLiteral(Token token)
    {
        var properties = new Dictionary<string, ExpressionNode>();
        SkipNewlines();
        if (Peek().Type != TokenType.RightBrace)
        {
            do
            {
                SkipNewlines();
                var keyToken = Advance();
                if (keyToken.Type != TokenType.Word)
                    throw new Exception($"Expected property name, got {keyToken.Type}");

                SkipNewlines();
                if (!Match(TokenType.Colon))
                    throw new Exception("Expected ':'");

                SkipNewlines();
                var valExp = ParseExpression(Precedence.Lowest);
                if (valExp == null) throw new Exception("Expected expression for property value");

                properties[keyToken.Value] = valExp;
                SkipNewlines();
            } while (Match(TokenType.Comma));
        }

        if (!Match(TokenType.RightBrace))
            throw new Exception("Expected '}'");

        return new ObjectExpressionNode(properties);
    }

    private ExpressionNode ParseBinaryExpression(ExpressionNode left, Token token)
    {
        var precedence = _precedences[token.Type];
        var right = ParseExpression(precedence);
        if (right == null) throw new Exception($"Expected expression after {token.Value}");
        
        BinaryOperator op = BinaryOperator.Add;
        switch (token.Value)
        {
            case "+": op = BinaryOperator.Add; break;
            case "-": op = BinaryOperator.Subtract; break;
            case "*": op = BinaryOperator.Multiply; break;
            case "/": op = BinaryOperator.Divide; break;
            case "==": op = BinaryOperator.Equal; break;
            case "!=": op = BinaryOperator.NotEqual; break;
            case "<": op = BinaryOperator.LessThan; break;
            case ">": op = BinaryOperator.GreaterThan; break;
            case "<=": op = BinaryOperator.LessThanOrEqual; break;
            case ">=": op = BinaryOperator.GreaterThanOrEqual; break;
            case "&&": op = BinaryOperator.LogicalAnd; break;
            case "||": op = BinaryOperator.LogicalOr; break;
        }
        
        return new BinaryExpressionNode(left, op, right);
    }

    private ExpressionNode ParseAssignmentExpression(ExpressionNode left, Token token)
    {
        var right = ParseExpression(Precedence.Lowest); // right-associative
        if (right == null) throw new Exception("Expected expression after '='");
        return new AssignmentExpressionNode(left, right);
    }

    private ExpressionNode ParseCallExpression(ExpressionNode left, Token token)
    {
        if (token.HasLeadingSpace)
            throw new Exception("Space not allowed before '(' in call expression");

        var args = new List<ExpressionNode>();
        if (Peek().Type != TokenType.RightParen)
        {
            do
            {
                var arg = ParseExpression(Precedence.Lowest);
                if (arg != null) args.Add(arg);
            } while (Match(TokenType.Comma));
        }

        if (!Match(TokenType.RightParen))
            throw new Exception("Expected ')'");

        return new CallExpressionNode(left, args);
    }

    private ExpressionNode ParseIndexExpression(ExpressionNode left, Token token)
    {
        if (token.HasLeadingSpace)
            throw new Exception("Space not allowed before '[' in index expression");

        var indexExp = ParseExpression(Precedence.Lowest);
        if (indexExp == null) throw new Exception("Expected expression inside '['");

        if (!Match(TokenType.RightBracket))
            throw new Exception("Expected ']'");

        return new IndexExpressionNode(left, indexExp);
    }

    private ExpressionNode ParseMemberExpression(ExpressionNode left, Token token)
    {
        if (token.HasLeadingSpace)
            throw new Exception("Space not allowed before '.' in member expression");

        var propToken = Advance();
        if (propToken.HasLeadingSpace)
            throw new Exception("Space not allowed after '.' in member expression");

        if (propToken.Type != TokenType.Word)
            throw new Exception("Expected property name after '.'");

        return new MemberExpressionNode(left, propToken.Value);
    }
}
