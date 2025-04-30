namespace Lua.CodeAnalysis.Syntax.Nodes;

public record ReturnStatementNode(ExpressionNode[] Nodes, SourcePosition Position) : StatementNode(Position)
{
    public override TResult Accept<TContext, TResult>(ISyntaxNodeVisitor<TContext, TResult> visitor, TContext context)
    {
        return visitor.VisitReturnStatementNode(this, context);
    }
}