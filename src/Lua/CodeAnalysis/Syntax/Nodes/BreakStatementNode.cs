namespace Lua.CodeAnalysis.Syntax.Nodes;

public record BreakStatementNode(SourcePosition Position) : StatementNode(Position)
{
    public override TResult Accept<TContext, TResult>(ISyntaxNodeVisitor<TContext, TResult> visitor, TContext context)
    {
        return visitor.VisitBreakStatementNode(this, context);
    }
}