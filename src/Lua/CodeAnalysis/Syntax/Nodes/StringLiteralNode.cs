namespace Lua.CodeAnalysis.Syntax.Nodes;

public record StringLiteralNode(ReadOnlyMemory<char> Text, bool IsShortLiteral, SourcePosition Position) : ExpressionNode(Position)
{
    public override TResult Accept<TContext, TResult>(ISyntaxNodeVisitor<TContext, TResult> visitor, TContext context)
    {
        return visitor.VisitStringLiteralNode(this, context);
    }
}