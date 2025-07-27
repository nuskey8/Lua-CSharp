using Microsoft.CodeAnalysis;

namespace Lua.SourceGenerator;

public sealed class SymbolReferences
{
    public static SymbolReferences? Create(Compilation compilation)
    {
        var luaObjectAttribute = compilation.GetTypeByMetadataName("Lua.LuaObjectAttribute");
        if (luaObjectAttribute == null)
        {
            return null;
        }

        return new()
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

    public INamedTypeSymbol LuaObjectAttribute { get; private set; } = null!;
    public INamedTypeSymbol LuaMemberAttribute { get; private set; } = null!;
    public INamedTypeSymbol LuaIgnoreMemberAttribute { get; private set; } = null!;
    public INamedTypeSymbol LuaMetamethodAttribute { get; private set; } = null!;
    public INamedTypeSymbol LuaValue { get; private set; } = null!;
    public INamedTypeSymbol Boolean { get; private set; } = null!;
    public INamedTypeSymbol String { get; private set; } = null!;
    public INamedTypeSymbol Double { get; private set; } = null!;
    public INamedTypeSymbol LuaFunction { get; private set; } = null!;
    public INamedTypeSymbol LuaThread { get; private set; } = null!;
    public INamedTypeSymbol LuaTable { get; private set; } = null!;
    public INamedTypeSymbol LuaUserData { get; private set; } = null!;
    public INamedTypeSymbol CancellationToken { get; private set; } = null!;
}