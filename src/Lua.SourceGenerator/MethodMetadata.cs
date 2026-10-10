using Microsoft.CodeAnalysis;

namespace Lua.SourceGenerator;

class MethodMetadata
{
    public IMethodSymbol Symbol { get; }
    public bool IsStatic { get; }
    public bool IsAsync { get; }
    public bool HasReturnValue { get; }
    public bool HasMemberAttribute { get; set; }
    public bool HasMetamethodAttribute { get; set; }
    public bool HasCancellationTokenParameter { get; }
    public bool HasParamsLikeParameter { get; }
    public bool ParamsAsMemory { get; }
    public int FixedParameterCount { get; }
    public string LuaMemberName { get; }
    public LuaObjectMetamethod Metamethod { get; }

    public MethodMetadata(IMethodSymbol symbol, SymbolReferences references)
    {
        Symbol = symbol;
        IsStatic = symbol.IsStatic;

        var returnType = symbol.ReturnType;
        // Arrays/pointers have no containing namespace; type parameters are not awaitable types.
        var fullName =
            returnType is INamedTypeSymbol namedType && namedType.ContainingNamespace is { } ns
                ? (ns.IsGlobalNamespace ? "" : ns + ".") + namedType.Name
                : null;
        IsAsync =
            fullName
                is "System.Threading.Tasks.Task"
                    or "System.Threading.Tasks.ValueTask"
                    or "Cysharp.Threading.Tasks.UniTask"
                    or "UnityEngine.Awaitable";

        HasReturnValue =
            !symbol.ReturnsVoid
            && !(IsAsync && returnType is INamedTypeSymbol n && !n.IsGenericType);

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
            Metamethod = (LuaObjectMetamethod)
                Enum.Parse(
                    typeof(LuaObjectMetamethod),
                    metamethodAttribute.ConstructorArguments[0].Value!.ToString()
                );
        }

        var parameters = symbol.Parameters;
        var parameterCount = parameters.Length;
        if (
            parameterCount > 0
            && parameters[parameterCount - 1].RefKind == RefKind.None
            && SymbolEqualityComparer.Default.Equals(
                parameters[parameterCount - 1].Type,
                references.CancellationToken
            )
        )
        {
            HasCancellationTokenParameter = true;
            parameterCount--;
        }

        // A borrowed argument view must be last, except for the injected cancellation token.
        // In particular, a token-only method has no preceding parameter to inspect.
        if (
            parameterCount > 0
            && parameters[parameterCount - 1].RefKind == RefKind.None
            && parameters[parameterCount - 1].Type
                is INamedTypeSymbol { TypeArguments.Length: 1 } parameterType
            && SymbolEqualityComparer.Default.Equals(
                parameterType.TypeArguments[0],
                references.LuaValue
            )
        )
        {
            ParamsAsMemory = SymbolEqualityComparer.Default.Equals(
                parameterType.OriginalDefinition,
                references.ReadOnlyMemory
            );
            HasParamsLikeParameter =
                ParamsAsMemory
                || SymbolEqualityComparer.Default.Equals(
                    parameterType.OriginalDefinition,
                    references.ReadOnlySpan
                );
            if (HasParamsLikeParameter)
            {
                parameterCount--;
            }
        }

        FixedParameterCount = parameterCount;
    }
}
