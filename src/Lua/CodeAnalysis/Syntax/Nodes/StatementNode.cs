namespace Lua.CodeAnalysis.Syntax.Nodes;

public abstract record StatementNode(SourcePosition Position) : SyntaxNode(Position);