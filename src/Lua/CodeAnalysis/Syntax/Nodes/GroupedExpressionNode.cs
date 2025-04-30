namespace Lua.CodeAnalysis.Syntax.Nodes;

public record GroupedExpressionNode(ExpressionNode Expression, SourcePosition Position) : ExpressionNode(Position)
{
    public override TResult Accept<TContext, TResult>(ISyntaxNodeVisitor<TContext, TResult> visitor, TContext context)
    {
        return visitor.VisitGroupedExpressionNode(this, context);
    }
}