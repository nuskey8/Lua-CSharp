namespace Lua.CodeAnalysis.Syntax.Nodes;

public record CallTableMethodStatementNode(CallTableMethodExpressionNode Expression) : StatementNode(Expression.Position)
{
    public override TResult Accept<TContext, TResult>(ISyntaxNodeVisitor<TContext, TResult> visitor, TContext context)
    {
        return visitor.VisitCallTableMethodStatementNode(this, context);
    }
}