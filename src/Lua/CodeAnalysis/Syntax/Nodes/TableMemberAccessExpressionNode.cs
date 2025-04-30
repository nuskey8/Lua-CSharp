namespace Lua.CodeAnalysis.Syntax.Nodes;

public record TableMemberAccessExpressionNode(ExpressionNode TableNode, string MemberName, SourcePosition Position) : ExpressionNode(Position)
{
    public override TResult Accept<TContext, TResult>(ISyntaxNodeVisitor<TContext, TResult> visitor, TContext context)
    {
        return visitor.VisitTableMemberAccessExpressionNode(this, context);
    }
}