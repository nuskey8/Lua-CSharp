namespace Lua.CodeAnalysis.Syntax.Nodes;

public record NumericLiteralNode(double Value, SourcePosition Position) : ExpressionNode(Position)
{
    public override TResult Accept<TContext, TResult>(ISyntaxNodeVisitor<TContext, TResult> visitor, TContext context)
    {
        return visitor.VisitNumericLiteralNode(this, context);
    }
}