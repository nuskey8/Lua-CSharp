namespace Lua.CodeAnalysis.Syntax.Nodes;

public record CallFunctionExpressionNode(ExpressionNode FunctionNode, ExpressionNode[] ArgumentNodes) : ExpressionNode(FunctionNode.Position)
{
    public override TResult Accept<TContext, TResult>(ISyntaxNodeVisitor<TContext, TResult> visitor, TContext context)
    {
        return visitor.VisitCallFunctionExpressionNode(this, context);
    }
}