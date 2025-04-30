namespace Lua.CodeAnalysis.Syntax.Nodes;

public record CallFunctionStatementNode(CallFunctionExpressionNode Expression) : StatementNode(Expression.Position)
{
    public override TResult Accept<TContext, TResult>(ISyntaxNodeVisitor<TContext, TResult> visitor, TContext context)
    {
        return visitor.VisitCallFunctionStatementNode(this, context);
    }
}