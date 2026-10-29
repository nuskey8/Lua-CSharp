namespace Lua;

sealed class LuaUserData : ILuaUserData
{
    public LuaTable? Metatable { get; set; }
    readonly LuaValue[] userValues = new LuaValue[1];

    public Span<LuaValue> UserValues => userValues;

    public LuaUserData(LuaValue value, LuaTable? metatable)
    {
        userValues[0] = value;
        Metatable = metatable;
    }
}

public interface ILuaUserData
{
    LuaTable? Metatable { get; set; }

    /// <summary>Gets the metatable associated with this state's globals. Defaults to <see cref="Metatable"/>.</summary>
    LuaTable? GetMetatable(LuaState state) => Metatable;

    // We use span for compatibility with lua5.4.
    Span<LuaValue> UserValues => default;
}
