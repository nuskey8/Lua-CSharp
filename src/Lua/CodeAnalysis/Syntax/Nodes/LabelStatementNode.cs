namespace Lua.CodeAnalysis.Syntax.Nodes;

public record LabelStatementNode(ReadOnlyMemory<char> Name, SourcePosition Position) : StatementNode(Position)
{
    public override TResult Accept<TContext, TResult>(ISyntaxNodeVisitor<TContext, TResult> visitor, TContext context)
    {
        return visitor.VisitLabelStatementNode(this, context);
    }
}