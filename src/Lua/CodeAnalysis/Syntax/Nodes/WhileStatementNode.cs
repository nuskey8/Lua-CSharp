namespace Lua.CodeAnalysis.Syntax.Nodes;

public record WhileStatementNode(ExpressionNode ConditionNode, SyntaxNode[] Nodes, SourcePosition Position) : StatementNode(Position)
{
    public override TResult Accept<TContext, TResult>(ISyntaxNodeVisitor<TContext, TResult> visitor, TContext context)
    {
        return visitor.VisitWhileStatementNode(this, context);
    }
}