namespace Lua;

/// <summary>The string representation used by a Lua state and its streams.</summary>
public enum LuaStringMode
{
    /// <summary>Strings contain UTF-16 code units. This is the default.</summary>
    Utf16,

    /// <summary>Each char holds one byte, including arbitrary binary data.</summary>
    Bytes,
}
