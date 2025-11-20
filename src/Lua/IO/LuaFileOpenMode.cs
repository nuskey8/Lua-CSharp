namespace Lua.IO;

public enum LuaFileOpenMode
{
    /// <summary>
    /// r
    /// </summary>
    Read,

    /// <summary>
    /// w
    /// </summary>
    Write,

    /// <summary>
    /// a
    /// </summary>
    Append,

    /// <summary>
    /// r+
    /// </summary>
    ReadUpdate,

    /// <summary>
    /// w+
    /// </summary>
    WriteUpdate,

    /// <summary>
    /// a+
    /// </summary>
    AppendUpdate
}

public static class LuaFileOpenModeExtensions
{
    public static bool CanRead(this LuaFileOpenMode mode)
    {
        return mode is LuaFileOpenMode.Read
            or LuaFileOpenMode.ReadUpdate
            or LuaFileOpenMode.WriteUpdate
            or LuaFileOpenMode.AppendUpdate;
    }

    public static bool CanWrite(this LuaFileOpenMode mode)
    {
        return mode is LuaFileOpenMode.Write
            or LuaFileOpenMode.ReadUpdate
            or LuaFileOpenMode.WriteUpdate
            or LuaFileOpenMode.Append
            or LuaFileOpenMode.AppendUpdate;
    }

    public static LuaFileOpenMode ParseModeFromString(string mode)
    {
        return mode switch
        {
            "r" => LuaFileOpenMode.Read,
            "rb" => LuaFileOpenMode.Read,
            "w" => LuaFileOpenMode.Write,
            "wb" => LuaFileOpenMode.Write,
            "a" => LuaFileOpenMode.Append,
            "ab" => LuaFileOpenMode.Append,
            "r+" => LuaFileOpenMode.ReadUpdate,
            "r+b" => LuaFileOpenMode.ReadUpdate,
            "w+" => LuaFileOpenMode.WriteUpdate,
            "w+b" => LuaFileOpenMode.WriteUpdate,
            "a+" => LuaFileOpenMode.AppendUpdate,
            "a+b" => LuaFileOpenMode.AppendUpdate,
            _ => 0
        };
    }


    public static bool IsValid(this LuaFileOpenMode mode)
    {
        return mode is LuaFileOpenMode.Read
            or LuaFileOpenMode.Write
            or LuaFileOpenMode.Append
            or LuaFileOpenMode.ReadUpdate
            or LuaFileOpenMode.WriteUpdate
            or LuaFileOpenMode.AppendUpdate;
    }

    public static bool IsAppend(this LuaFileOpenMode mode)
    {
        return mode is LuaFileOpenMode.Append or LuaFileOpenMode.AppendUpdate or LuaFileOpenMode.WriteUpdate;
    }

    public static void ThrowIfNotReadable(this LuaFileOpenMode mode)
    {
        if (!mode.CanRead())
        {
            throw new IOException($"Cannot read from a file opened with mode {mode}");
        }
    }

    public static void ThrowIfNotWritable(this LuaFileOpenMode mode)
    {
        if (!mode.CanWrite())
        {
            throw new IOException($"Cannot write to a file opened with mode {mode}");
        }
    }
}