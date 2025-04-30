namespace Lua.CodeAnalysis.Syntax.Nodes;

public record NilLiteralNode(SourcePosition Position) : ExpressionNode(Position)
{
    public override TResult Accept<TContext, TResult>(ISyntaxNodeVisitor<TContext, TResult> visitor, TContext context)
    {
        return visitor.VisitNilLiteralNode(this, context);
    }
}