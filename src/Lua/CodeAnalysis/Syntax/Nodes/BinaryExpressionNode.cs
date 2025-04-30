namespace Lua.CodeAnalysis.Syntax.Nodes;

public enum BinaryOperator
{
    Addition,
    Subtraction,
    Multiplication,
    Division,
    Modulo,
    Exponentiation,
    Equality,
    Inequality,
    GreaterThan,
    GreaterThanOrEqual,
    LessThan,
    LessThanOrEqual,
    And,
    Or,
    Concat,
}

internal static class BinaryOperatorEx
{
    public static string ToDisplayString(this BinaryOperator @operator)
    {
        return @operator switch
        {
            BinaryOperator.Addition => Keywords.Addition,
            BinaryOperator.Subtraction => Keywords.Subtraction,
            BinaryOperator.Multiplication => Keywords.Multiplication,
            BinaryOperator.Division => Keywords.Division,
            BinaryOperator.Modulo => Keywords.Modulo,
            BinaryOperator.Exponentiation => Keywords.Exponentiation,
            BinaryOperator.Equality => Keywords.Equality,
            BinaryOperator.Inequality => Keywords.Inequality,
            BinaryOperator.GreaterThan => Keywords.GreaterThan,
            BinaryOperator.GreaterThanOrEqual => Keywords.GreaterThanOrEqual,
            BinaryOperator.LessThan => Keywords.LessThan,
            BinaryOperator.LessThanOrEqual => Keywords.LessThanOrEqual,
            BinaryOperator.And => Keywords.And,
            BinaryOperator.Or => Keywords.Or,
            BinaryOperator.Concat => Keywords.Concat,
            _ => "",
        };
    }
}

public record BinaryExpressionNode(BinaryOperator OperatorType, ExpressionNode LeftNode, ExpressionNode RightNode, SourcePosition Position) : ExpressionNode(Position)
{
    public override TResult Accept<TContext, TResult>(ISyntaxNodeVisitor<TContext, TResult> visitor, TContext context)
    {
        return visitor.VisitBinaryExpressionNode(this, context);
    }
}