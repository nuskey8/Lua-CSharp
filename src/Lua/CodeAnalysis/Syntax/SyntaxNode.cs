namespace Lua.CodeAnalysis.Syntax;

public abstract record SyntaxNode(SourcePosition Position)
{
    public abstract TResult Accept<TContext, TResult>(ISyntaxNodeVisitor<TContext, TResult> visitor, TContext context);
}