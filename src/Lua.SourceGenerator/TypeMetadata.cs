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

    public TypeMetadata(
        TypeDeclarationSyntax syntax,
        INamedTypeSymbol symbol,
        SymbolReferences references
    )
    {
        Syntax = syntax;
        Symbol = symbol;

        TypeName = symbol.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
        FullTypeName = symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

        var properties = new List<PropertyMetadata>();
        var methods = new List<MethodMetadata>();
        var memberOwners = new Dictionary<string, INamedTypeSymbol>();
        var metamethodOwners = new Dictionary<LuaObjectMetamethod, INamedTypeSymbol>();

        foreach (var member in Symbol.GetAllMembers())
        {
            if (
                member.IsImplicitlyDeclared
                || member.ContainsAttribute(references.LuaIgnoreMemberAttribute)
            )
            {
                continue;
            }

            if (member is IFieldSymbol or IPropertySymbol)
            {
                if (
                    !member.ContainsAttribute(references.LuaMemberAttribute)
                    || member is IPropertySymbol { IsIndexer: true }
                )
                {
                    continue;
                }

                var property = new PropertyMetadata(member, references);
                if (SelectBinding(memberOwners, property.LuaMemberName, member))
                {
                    properties.Add(property);
                }
            }
            else if (member is IMethodSymbol methodSymbol)
            {
                if (
                    !member.ContainsAttribute(references.LuaMemberAttribute)
                    && !member.ContainsAttribute(references.LuaMetamethodAttribute)
                )
                {
                    continue;
                }

                var method = new MethodMetadata(methodSymbol, references);
                // A method's member and metamethod bindings can be replaced independently.
                if (method.HasMemberAttribute)
                {
                    method.HasMemberAttribute = SelectBinding(
                        memberOwners,
                        method.LuaMemberName,
                        member
                    );
                }
                if (method.HasMetamethodAttribute)
                {
                    method.HasMetamethodAttribute = SelectBinding(
                        metamethodOwners,
                        method.Metamethod,
                        member
                    );
                }
                if (method.HasMemberAttribute || method.HasMetamethodAttribute)
                {
                    methods.Add(method);
                }
            }
        }

        Properties = properties.ToArray();
        Methods = methods.ToArray();
    }

    static bool SelectBinding<TKey>(
        Dictionary<TKey, INamedTypeSymbol> owners,
        TKey key,
        ISymbol member
    )
        where TKey : notnull
    {
        if (!owners.TryGetValue(key, out var owner))
        {
            owners.Add(key, member.ContainingType);
            return true;
        }

        // Derived bindings replace inherited ones. Keep duplicates within one type
        // so that invalid declarations still produce diagnostics.
        return SymbolEqualityComparer.Default.Equals(owner, member.ContainingType);
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
