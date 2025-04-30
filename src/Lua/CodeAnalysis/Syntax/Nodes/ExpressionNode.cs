namespace Lua.CodeAnalysis.Syntax.Nodes;

public abstract record ExpressionNode(SourcePosition Position) : SyntaxNode(Position);