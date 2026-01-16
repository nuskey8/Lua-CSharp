using Microsoft.CodeAnalysis;

namespace Lua.SourceGenerator;

public class PropertyMetadata
{
    public ISymbol Symbol { get; }
    public ITypeSymbol Type { get; }
    public string TypeFullName { get; }
    public bool IsStatic { get; }
    public bool IsReadOnly { get; }
    public bool IsWriteOnly { get; }
    public string LuaMemberName { get; }

    public PropertyMetadata(ISymbol symbol, SymbolReferences references)
    {
        Symbol = symbol;

        IsStatic = symbol.IsStatic;

        if (symbol is IFieldSymbol field)
        {
            Type = field.Type;
            TypeFullName = field.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            IsReadOnly = field.IsReadOnly;
        }
        else if (symbol is IPropertySymbol property)
        {
            Type = property.Type;
            TypeFullName = property.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            IsReadOnly = property.IsReadOnly || property.SetMethod == null || property.SetMethod.IsInitOnly;
            IsWriteOnly = property.IsWriteOnly;
        }
        else
        {
            Type = default!;
            TypeFullName = "";
        }

        LuaMemberName = symbol.Name;

        var memberAttribute = symbol.GetAttribute(references.LuaMemberAttribute);
        if (memberAttribute != null)
        {
            if (memberAttribute.ConstructorArguments.Length > 0)
            {
                var value = memberAttribute.ConstructorArguments[0].Value;
                if (value is string str)
                {
                    LuaMemberName = str;
                }
            }
        }
    }
}