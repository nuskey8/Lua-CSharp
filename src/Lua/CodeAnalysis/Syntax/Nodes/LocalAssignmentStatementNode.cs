namespace Lua.CodeAnalysis.Syntax.Nodes;

public record LocalAssignmentStatementNode(IdentifierNode[] Identifiers, ExpressionNode[] RightNodes, SourcePosition Position) : AssignmentStatementNode(Identifiers, RightNodes, Position)
{
    public override TResult Accept<TContext, TResult>(ISyntaxNodeVisitor<TContext, TResult> visitor, TContext context)
    {
        return visitor.VisitLocalAssignmentStatementNode(this, context);
    }
}