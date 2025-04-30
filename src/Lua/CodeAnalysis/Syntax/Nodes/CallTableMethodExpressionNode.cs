namespace Lua.CodeAnalysis.Syntax.Nodes;

public record CallTableMethodExpressionNode(ExpressionNode TableNode, string MethodName, ExpressionNode[] ArgumentNodes, SourcePosition Position) : ExpressionNode(Position)
{
    public override TResult Accept<TContext, TResult>(ISyntaxNodeVisitor<TContext, TResult> visitor, TContext context)
    {
        return visitor.VisitCallTableMethodExpressionNode(this, context);
    }
}