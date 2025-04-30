namespace Lua.CodeAnalysis.Syntax.Nodes;

public enum UnaryOperator
{
    Negate,
    Not,
    Length,
}

public record UnaryExpressionNode(UnaryOperator Operator, ExpressionNode Node, SourcePosition Position) : ExpressionNode(Position)
{
    public override TResult Accept<TContext, TResult>(ISyntaxNodeVisitor<TContext, TResult> visitor, TContext context)
    {
        return visitor.VisitUnaryExpressionNode(this, context);
    }
}

internal static class UnaryOperatorEx
{
    public static string ToDisplayString(this UnaryOperator @operator)
    {
        return @operator switch
        {
            UnaryOperator.Negate => Keywords.Subtraction,
            UnaryOperator.Not => Keywords.Not,
            UnaryOperator.Length => Keywords.Length,
            _ => "",
        };
    }
}