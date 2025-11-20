using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Lua.SourceGenerator;

class TypeMetadata
{
    public TypeDeclarationSyntax Syntax { get; }
    public INamedTypeSymbol Symbol { get; }
    public string TypeName { get; }
    public string FullTypeName { get; }
    public PropertyMetadata[] Properties { get; }
    public MethodMetadata[] Methods { get; }

    public TypeMetadata(TypeDeclarationSyntax syntax, INamedTypeSymbol symbol, SymbolReferences references)
    {
        Syntax = syntax;
        Symbol = symbol;

        TypeName = symbol.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
        FullTypeName = symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

        Properties = Symbol.GetAllMembers(false)
            .Where(x => x is (IFieldSymbol or IPropertySymbol) and { IsImplicitlyDeclared: false })
            .Where(x =>
            {
                if (!x.ContainsAttribute(references.LuaMemberAttribute))
                {
                    return false;
                }

                if (x.ContainsAttribute(references.LuaIgnoreMemberAttribute))
                {
                    return false;
                }

                if (x is IPropertySymbol p)
                {
                    if (p.GetMethod == null || p.SetMethod == null)
                    {
                        return false;
                    }

                    if (p.IsIndexer)
                    {
                        return false;
                    }
                }

                return true;
            })
            .Select(x => new PropertyMetadata(x, references))
            .ToArray();

        Methods = Symbol.GetAllMembers(false)
            .Where(x => x is IMethodSymbol and { IsImplicitlyDeclared: false })
            .Select(x => (IMethodSymbol)x)
            .Where(x =>
            {
                if (x.ContainsAttribute(references.LuaIgnoreMemberAttribute))
                {
                    return false;
                }

                return x.ContainsAttribute(references.LuaMemberAttribute) || x.ContainsAttribute(references.LuaMetamethodAttribute);
            })
            .Select(x => new MethodMetadata(x, references))
            .ToArray();
    }

    public bool IsPartial()
    {
        return Syntax.Modifiers.Any(m => m.IsKind(SyntaxKind.PartialKeyword));
    }

    public bool IsNested()
    {
        return Syntax.Parent is TypeDeclarationSyntax;
    }
}