using Microsoft.CodeAnalysis;

namespace Lua.SourceGenerator;

static class SymbolExtensions
{
    public static bool ContainsAttribute(this ISymbol symbol, INamedTypeSymbol attribtue)
    {
        return symbol
            .GetAttributes()
            .Any(x => SymbolEqualityComparer.Default.Equals(x.AttributeClass, attribtue));
    }

    public static AttributeData? GetAttribute(this ISymbol symbol, INamedTypeSymbol attribtue)
    {
        return symbol
            .GetAttributes()
            .FirstOrDefault(x =>
                SymbolEqualityComparer.Default.Equals(x.AttributeClass, attribtue)
            );
    }

    public static IEnumerable<ISymbol> GetAllMembers(this INamedTypeSymbol symbol)
    {
        // Include overrides at every level, with the most derived declarations first.
        for (var current = symbol; current != null; current = current.BaseType)
        {
            foreach (var member in current.GetMembers())
            {
                yield return member;
            }
        }
    }
}
