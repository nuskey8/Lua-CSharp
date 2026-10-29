namespace Lua;

/// <summary>The numeric text representation used by Lua execution.</summary>
public enum LuaNumberFormat
{
    /// <summary>Invariant .NET formatting. This is the default.</summary>
    DotNet,

    /// <summary>Lua 5.2's default C-locale 14 significant digits and portable signed inf/nan spellings.</summary>
    Lua,
}
