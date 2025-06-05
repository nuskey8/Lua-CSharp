namespace Lua.IO;

[Flags]
public enum LuaFileMode
{
    None = 0,

    // Access modes (mutually exclusive)
    Read = 1 << 0, // r
    Write = 1 << 1, // w  
    Append = 1 << 2, // a
    Update = 1 << 3, // +

    // Content type flags
    Binary = 1 << 4, // b
    Text = 1 << 5, // t (default if neither specified)

    // Common combinations
    ReadBinary = Read | Binary, // rb
    WriteBinary = Write | Binary, // wb
    AppendBinary = Append | Binary, // ab
    ReadText = Read | Text, // r
    WriteText = Write | Text, // w
    AppendText = Append | Text, //  a

    ReadUpdate = Read | Update, // r+
    WriteUpdate = Write | Update, // w+
    AppendUpdate = Append | Update, // a+
    ReadUpdateText = Read | Update | Text, // r+
    WriteUpdateText = Write | Update | Text, // w+
    AppendUpdateText = Append | Update | Text, // a+

    ReadUpdateBinary = Read | Update | Binary, // r+b or rb+
    WriteUpdateBinary = Write | Update | Binary, // w+b or wb+
    AppendUpdateBinary = Append | Update | Binary, // a+b or ab+

    /// <summary>
    /// This is used for load files. bt mode.
    /// </summary>
    ReadBinaryOrText = Read | Binary | Text, //(default is text, but can be binary)
}

public static class LuaFileModeExtensions
{
    public static LuaFileMode GetMode(LuaFileOpenMode openMode, LuaFileContentType type)
    {
        var isBinary = type == LuaFileContentType.Binary;
        return openMode switch
        {
            LuaFileOpenMode.Read => isBinary ? LuaFileMode.ReadBinary : LuaFileMode.ReadText,
            LuaFileOpenMode.Write => isBinary ? LuaFileMode.WriteBinary : LuaFileMode.WriteText,
            LuaFileOpenMode.Append => isBinary ? LuaFileMode.AppendBinary : LuaFileMode.AppendText,
            LuaFileOpenMode.ReadWriteOpen => isBinary ? LuaFileMode.ReadUpdateBinary : LuaFileMode.ReadUpdateText,
            LuaFileOpenMode.ReadWriteCreate => isBinary ? LuaFileMode.WriteUpdateBinary : LuaFileMode.WriteUpdateText,
            LuaFileOpenMode.ReadAppend => isBinary ? LuaFileMode.AppendUpdateBinary : LuaFileMode.AppendUpdateText,
            _ => throw new ArgumentOutOfRangeException(nameof(openMode), openMode, "Invalid file open mode")
        };
    }

    public static LuaFileOpenMode GetOpenMode(this LuaFileMode mode)
    {
        var hasUpdate = (mode & LuaFileMode.Update) != 0;

        if ((mode & LuaFileMode.Read) != 0)
            return hasUpdate ? LuaFileOpenMode.ReadWriteOpen : LuaFileOpenMode.Read;
        if ((mode & LuaFileMode.Write) != 0)
            return hasUpdate ? LuaFileOpenMode.ReadWriteCreate : LuaFileOpenMode.Write;
        if ((mode & LuaFileMode.Append) != 0)
            return hasUpdate ? LuaFileOpenMode.ReadAppend : LuaFileOpenMode.Append;

        throw new ArgumentException("Invalid file open flags: no access mode specified", nameof(mode));
    }

    public static LuaFileContentType GetContentType(this LuaFileMode mode)
    {
        // If binary flag is set, it's binary mode
        if ((mode & LuaFileMode.Binary) != 0)
            return LuaFileContentType.Binary;

        // Otherwise it's text mode (even if Text flag is not explicitly set)
        return LuaFileContentType.Text;
    }

    public static LuaFileMode ParseModeString(string mode)
    {
        var flags = LuaFileMode.None;

        // Parse base mode
        if (mode.Contains("+"))
            flags |= LuaFileMode.Update;
        if (mode.Contains("r"))
            flags |= LuaFileMode.Read;
        if (mode.Contains("w"))
            flags |= LuaFileMode.Write;
        if (mode.Contains("a"))
            flags |= LuaFileMode.Append;

        // Parse content type
        if (mode.Contains('b'))
            flags |= LuaFileMode.Binary;
        else
            flags |= LuaFileMode.Text;
        // If neither 'b' nor 't' is specified, default is text (handled by GetContentType)

        return flags;
    }

    public static bool IsValid(this LuaFileMode mode)
    {
        var modeCount = 0;
        if ((mode & LuaFileMode.Read) != 0) modeCount++;
        if ((mode & LuaFileMode.Write) != 0) modeCount++;
        if ((mode & LuaFileMode.Append) != 0) modeCount++;
        if (modeCount != 1)
        {
            return false; // Must have exactly one access mode
        }

        var typeCount = 0;
        if ((mode & LuaFileMode.Binary) != 0) typeCount++;
        if ((mode & LuaFileMode.Text) != 0) typeCount++;
        if (typeCount < 1)
        {
            return false;
        }

        return true;
    }

    public static bool IsAppend(this LuaFileMode mode)
    {
        return (mode & LuaFileMode.Append) != 0;
    }

    public static bool CanRead(this LuaFileMode mode)
    {
        return (mode & LuaFileMode.Read) != 0;
    }

    public static bool CanWrite(this LuaFileMode mode)
    {
        return (mode & LuaFileMode.Write) != 0 || (mode & LuaFileMode.Append) != 0;
    }

    public static bool IsBinary(this LuaFileMode mode)
    {
        return (mode & LuaFileMode.Binary) != 0;
    }

    public static bool IsText(this LuaFileMode mode)
    {
        return (mode & LuaFileMode.Text) != 0;
    }


    public static void ThrowIfNotValid(this LuaFileMode mode)
    {
        if (!mode.IsValid())
        {
            throw new ArgumentException("Invalid file mode flags", nameof(mode));
        }
    }

    public static void ThrowIfNotReadable(this LuaFileMode mode)
    {
        if (!mode.CanRead())
        {
            throw new InvalidOperationException("This operation is only valid for readable streams.");
        }
    }

    public static void ThrowIfNotWritable(this LuaFileMode mode)
    {
        if (!mode.CanWrite())
        {
            throw new InvalidOperationException("This operation is only valid for writable streams.");
        }
    }

    public static void ThrowIfNotText(this LuaFileMode mode)
    {
        if (!mode.IsText())
        {
            throw new InvalidOperationException("This operation is only valid for text streams.");
        }
    }

    public static void ThrowIfNotBinary(this LuaFileMode mode)
    {
        if (!mode.IsBinary())
        {
            throw new InvalidOperationException("This operation is only valid for binary streams.");
        }
    }
}