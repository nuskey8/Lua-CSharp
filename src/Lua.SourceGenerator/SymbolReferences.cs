using Microsoft.CodeAnalysis;

namespace Lua.SourceGenerator;

public sealed class SymbolReferences
{
    public static SymbolReferences? Create(Compilation compilation)
    {
        var luaObjectAttribute = compilation.GetTypeByMetadataName("Lua.LuaObjectAttribute");
        if (luaObjectAttribute == null) return null;
        return new SymbolReferences
        {
            LuaObjectAttribute = luaObjectAttribute,
            LuaMemberAttribute = compilation.GetTypeByMetadataName("Lua.LuaMemberAttribute")!,
            LuaIgnoreMemberAttribute = compilation.GetTypeByMetadataName("Lua.LuaIgnoreMemberAttribute")!,
            LuaMetamethodAttribute = compilation.GetTypeByMetadataName("Lua.LuaMetamethodAttribute")!,
            LuaValue = compilation.GetTypeByMetadataName("Lua.LuaValue")!,
            Boolean = compilation.GetTypeByMetadataName("System.Boolean")!,
            String = compilation.GetTypeByMetadataName("System.String")!,
            Double = compilation.GetTypeByMetadataName("System.Double")!,
            LuaFunction = compilation.GetTypeByMetadataName("Lua.LuaFunction")!,
            LuaThread = compilation.GetTypeByMetadataName("Lua.LuaThread")!,
            LuaTable = compilation.GetTypeByMetadataName("Lua.LuaTable")!,
            LuaUserData = compilation.GetTypeByMetadataName("Lua.ILuaUserData")!,
            CancellationToken = compilation.GetTypeByMetadataName("System.Threading.CancellationToken")!
        };
    }

    public INamedTypeSymbol LuaObjectAttribute { get; private set; } = default!;
    public INamedTypeSymbol LuaMemberAttribute { get; private set; } = default!;
    public INamedTypeSymbol LuaIgnoreMemberAttribute { get; private set; } = default!;
    public INamedTypeSymbol LuaMetamethodAttribute { get; private set; } = default!;
    public INamedTypeSymbol LuaValue { get; private set; } = default!;
    public INamedTypeSymbol Boolean { get; private set; } = default!;
    public INamedTypeSymbol String { get; private set; } = default!;
    public INamedTypeSymbol Double { get; private set; } = default!;
    public INamedTypeSymbol LuaFunction { get; private set; } = default!;
    public INamedTypeSymbol LuaThread { get; private set; } = default!;
    public INamedTypeSymbol LuaTable { get; private set; } = default!;
    public INamedTypeSymbol LuaUserData { get; private set; } = default!;
    public INamedTypeSymbol CancellationToken { get; private set; } = default!;
}