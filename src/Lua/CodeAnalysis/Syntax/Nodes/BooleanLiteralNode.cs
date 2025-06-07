namespace Lua.CodeAnalysis.Syntax.Nodes;

public record BooleanLiteralNode(bool Value, SourcePosition Position) : ExpressionNode(Position)
{
    public override TResult Accept<TContext, TResult>(ISyntaxNodeVisitor<TContext, TResult> visitor, TContext context)
    {
        return visitor.VisitBooleanLiteralNode(this, context);
    }
}