namespace Lua.CodeAnalysis.Syntax.Nodes;

public record TableIndexerAccessExpressionNode(ExpressionNode TableNode, ExpressionNode KeyNode, SourcePosition Position) : ExpressionNode(Position)
{
    public override TResult Accept<TContext, TResult>(ISyntaxNodeVisitor<TContext, TResult> visitor, TContext context)
    {
        return visitor.VisitTableIndexerAccessExpressionNode(this, context);
    }
}