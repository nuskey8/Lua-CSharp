namespace Lua.CodeAnalysis.Syntax.Nodes;

public record GotoStatementNode(ReadOnlyMemory<char> Name, SourcePosition Position) : StatementNode(Position)
{
    public override TResult Accept<TContext, TResult>(ISyntaxNodeVisitor<TContext, TResult> visitor, TContext context)
    {
        return visitor.VisitGotoStatementNode(this, context);
    }
}