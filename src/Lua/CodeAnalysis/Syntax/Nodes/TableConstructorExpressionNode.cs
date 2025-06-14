namespace Lua.CodeAnalysis.Syntax.Nodes;

public record TableConstructorExpressionNode(TableConstructorField[] Fields, SourcePosition Position) : ExpressionNode(Position)
{
    public override TResult Accept<TContext, TResult>(ISyntaxNodeVisitor<TContext, TResult> visitor, TContext context)
    {
        return visitor.VisitTableConstructorExpressionNode(this, context);
    }
}

public abstract record TableConstructorField(SourcePosition Position);

public record GeneralTableConstructorField(ExpressionNode KeyExpression, ExpressionNode ValueExpression, SourcePosition Position) : TableConstructorField(Position);

public record RecordTableConstructorField(string Key, ExpressionNode ValueExpression, SourcePosition Position) : TableConstructorField(Position);

public record ListTableConstructorField(ExpressionNode Expression, SourcePosition Position) : TableConstructorField(Position);