namespace Lua.CodeAnalysis.Syntax.Nodes;

public record DoStatementNode(StatementNode[] StatementNodes, SourcePosition Position) : StatementNode(Position)
{
    public override TResult Accept<TContext, TResult>(ISyntaxNodeVisitor<TContext, TResult> visitor, TContext context)
    {
        return visitor.VisitDoStatementNode(this, context);
    }
}