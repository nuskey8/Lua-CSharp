namespace Lua.IO;

public enum LuaFileBufferingMode
{
    /// <summary>
    /// No buffering. `no` in Lua
    /// </summary>
    NoBuffering,

    /// <summary>
    /// Full buffering `full` in Lua
    /// </summary>
    FullBuffering,

    /// <summary>
    /// Line buffering `line` in Lua
    /// </summary>
    LineBuffering,
}