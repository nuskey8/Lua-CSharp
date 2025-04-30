namespace Lua.CodeAnalysis.Syntax.Nodes;

public record AssignmentStatementNode(SyntaxNode[] LeftNodes, ExpressionNode[] RightNodes, SourcePosition Position) : StatementNode(Position)
{
    public override TResult Accept<TContext, TResult>(ISyntaxNodeVisitor<TContext, TResult> visitor, TContext context)
    {
        return visitor.VisitAssignmentStatementNode(this, context);
    }
}