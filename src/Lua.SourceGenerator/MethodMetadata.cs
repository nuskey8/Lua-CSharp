using Microsoft.CodeAnalysis;

namespace Lua.SourceGenerator;

internal class MethodMetadata
{
    public IMethodSymbol Symbol { get; }
    public bool IsStatic { get; }
    public bool IsAsync { get; }
    public bool HasReturnValue { get; }
    public bool HasMemberAttribute { get; }
    public bool HasMetamethodAttribute { get; }
    public string LuaMemberName { get; }
    public LuaObjectMetamethod Metamethod { get; }

    public MethodMetadata(IMethodSymbol symbol, SymbolReferences references)
    {
        Symbol = symbol;
        IsStatic = symbol.IsStatic;

        var returnType = symbol.ReturnType;
        var isArray = returnType is IArrayTypeSymbol arrayType;
        var fullName = isArray ?
            "System.Array"
            : (returnType.ContainingNamespace.IsGlobalNamespace ? "" : (returnType.ContainingNamespace + ".")) + returnType.Name;
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
    }
}