using Microsoft.CodeAnalysis;

namespace Lua.SourceGenerator;

class MethodMetadata
{
    public IMethodSymbol Symbol { get; }
    public bool IsStatic { get; }
    public bool IsAsync { get; }
    public bool HasReturnValue { get; }
    public bool HasMemberAttribute { get; }
    public bool HasMetamethodAttribute { get; }
    public bool HasCancellationTokenParameter { get; }
    public bool HasParamsLikeParameter { get; }
    public bool ParamsAsMemory { get; }
    public string ParamsString => ParamsAsMemory ? "context.ArgumentsAsMemory" : "context.Arguments";
    public string LuaMemberName { get; }
    public LuaObjectMetamethod Metamethod { get; }

    public MethodMetadata(IMethodSymbol symbol, SymbolReferences references)
    {
        Symbol = symbol;
        IsStatic = symbol.IsStatic;

        var returnType = symbol.ReturnType;
        var fullName = (returnType.ContainingNamespace.IsGlobalNamespace ? "" : returnType.ContainingNamespace + ".") + returnType.Name;
        IsAsync = fullName is "System.Threading.Tasks.Task"
            or "System.Threading.Tasks.ValueTask"
            or "Cysharp.Threading.Tasks.UniTask"
            or "UnityEngine.Awaitable";

        HasReturnValue = !symbol.ReturnsVoid && !(IsAsync && returnType is INamedTypeSymbol n && !n.IsGenericType);

        LuaMemberName = symbol.Name;

        var memberAttribute = symbol.GetAttribute(references.LuaMemberAttribute);
        HasMemberAttribute = memberAttribute != null;

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

        var metamethodAttribute = symbol.GetAttribute(references.LuaMetamethodAttribute);
        HasMetamethodAttribute = metamethodAttribute != null;

        if (metamethodAttribute != null)
        {
            Metamethod = (LuaObjectMetamethod)Enum.Parse(typeof(LuaObjectMetamethod), metamethodAttribute.ConstructorArguments[0].Value!.ToString());
        }

        var parameters = symbol.Parameters;
        if (parameters.Length <= 0)
        {
            return;
        }

        if (SymbolEqualityComparer.Default.Equals(parameters[parameters.Length - 1].Type, references.CancellationToken))
        {
            HasCancellationTokenParameter = true;
        }

        var typeToCheck = parameters[parameters.Length - (HasCancellationTokenParameter ? 2 : 1)].Type;
        if (typeToCheck is INamedTypeSymbol { TypeArguments.Length: 1 } namedTypeSymbol)
        {
            if (!SymbolEqualityComparer.Default.Equals(namedTypeSymbol.OriginalDefinition, references.LuaValueSpan))
            {
                ParamsAsMemory = true;
                if (!SymbolEqualityComparer.Default.Equals(namedTypeSymbol.OriginalDefinition, references.LuaValueMemory))
                {
                    return;
                }
            }
            
            if (SymbolEqualityComparer.Default.Equals(namedTypeSymbol.TypeArguments[0], references.LuaValue))
            {
                HasParamsLikeParameter = true;
            }
        }
    }
}