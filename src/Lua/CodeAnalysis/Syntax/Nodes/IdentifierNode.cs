namespace Lua.CodeAnalysis.Syntax.Nodes;

public record IdentifierNode(ReadOnlyMemory<char> Name, SourcePosition Position) : ExpressionNode(Position)
{
    public override TResult Accept<TContext, TResult>(ISyntaxNodeVisitor<TContext, TResult> visitor, TContext context)
    {
        return visitor.VisitIdentifierNode(this, context);
    }
}