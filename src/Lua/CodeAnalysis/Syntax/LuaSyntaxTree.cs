namespace Lua.CodeAnalysis.Syntax;

public record LuaSyntaxTree(SyntaxNode[] Nodes, SourcePosition Position) : SyntaxNode(Position)
{
    public override TResult Accept<TContext, TResult>(ISyntaxNodeVisitor<TContext, TResult> visitor, TContext context)
    {
        return visitor.VisitSyntaxTree(this, context);
    }

    public static LuaSyntaxTree Parse(string source, string? chunkName = null)
    {
        Lexer lexer = new() { Source = source.AsMemory(), ChunkName = chunkName };

        Parser parser = new() { ChunkName = chunkName };

        while (lexer.MoveNext())
        {
            parser.Add(lexer.Current);
        }

        return parser.Parse();
    }
}