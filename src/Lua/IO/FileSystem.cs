namespace Lua.IO;

/// <param name="baseDirectory">Directory that relative paths are resolved against.</param>
/// <param name="byteStrings">
/// When true, files are read and written as raw bytes (one byte per char) instead of UTF-8 text.
/// Use together with <see cref="Lua.Platforms.LuaPlatform.ByteStrings"/>.
/// </param>
public sealed class FileSystem(string? baseDirectory = null, bool byteStrings = false)
    : ILuaFileSystem
{
    public FileSystem(string? baseDirectory)
        : this(baseDirectory, false) { }

    public string BaseDirectory => baseDirectory ?? Directory.GetCurrentDirectory();

    public static (FileMode, FileAccess access) GetFileMode(LuaFileOpenMode luaFileOpenMode)
    {
        return luaFileOpenMode switch
        {
            LuaFileOpenMode.Read => (FileMode.Open, FileAccess.Read),
            LuaFileOpenMode.Write => (FileMode.Create, FileAccess.Write),
            LuaFileOpenMode.Append => (FileMode.Append, FileAccess.Write),
            LuaFileOpenMode.ReadUpdate => (FileMode.Open, FileAccess.ReadWrite),
            LuaFileOpenMode.WriteUpdate => (FileMode.Create, FileAccess.ReadWrite),
            LuaFileOpenMode.AppendUpdate => (FileMode.Append, FileAccess.ReadWrite),
            _ => throw new ArgumentOutOfRangeException(
                nameof(luaFileOpenMode),
                luaFileOpenMode,
                null
            ),
        };
    }

    public string GetFullPath(string path)
    {
        if (baseDirectory == null || Path.IsPathRooted(path))
        {
            return path;
        }

        return Path.Combine(baseDirectory, path);
    }

    // Paths come from Lua as byte strings in byte mode: decode the UTF-8 bytes into the real file name.
    string ResolvePath(string path)
    {
        return GetFullPath(byteStrings ? LuaByteString.ToString(path) : path);
    }

    public bool IsReadable(string path)
    {
        return File.Exists(ResolvePath(path));
    }

    public ValueTask<ILuaStream> Open(
        string path,
        LuaFileOpenMode openMode,
        CancellationToken cancellationToken
    )
    {
        var (mode, access) = GetFileMode(openMode);
        Stream stream;
        path = ResolvePath(path);
        if (openMode == LuaFileOpenMode.AppendUpdate)
        {
            stream = File.Open(
                path,
                FileMode.OpenOrCreate,
                FileAccess.ReadWrite,
                FileShare.ReadWrite | FileShare.Delete
            );
        }
        else
        {
            stream = File.Open(path, mode, access, FileShare.ReadWrite | FileShare.Delete);
        }

        ILuaStream wrapper = new LuaStream(openMode, stream, byteStrings);

        if (openMode == LuaFileOpenMode.AppendUpdate)
        {
            wrapper.Seek(SeekOrigin.End, 0);
        }

        return new(wrapper);
    }

    public ValueTask Rename(string oldName, string newName, CancellationToken cancellationToken)
    {
        oldName = ResolvePath(oldName);
        newName = ResolvePath(newName);

        // Check the source first: a failed rename must leave the destination untouched.
        var isFile = File.Exists(oldName);
        if (!isFile && !Directory.Exists(oldName))
        {
            throw new FileNotFoundException("No such file or directory", oldName);
        }

        if (oldName == newName)
        {
            return default;
        }

        if (!isFile)
        {
            Directory.Move(oldName, newName);
            return default;
        }

        // Let the filesystem replace the destination only when the move succeeds.
#if NETSTANDARD2_1
        // .NET Standard does not expose the overwrite overload of File.Move.
        if (File.Exists(newName))
        {
            File.Replace(oldName, newName, null);
        }
        else
        {
            File.Move(oldName, newName);
        }
#else
        File.Move(oldName, newName, overwrite: true);
#endif
        return default;
    }

    public ValueTask Remove(string path, CancellationToken cancellationToken)
    {
        path = ResolvePath(path);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("No such file or directory", path);
        }

        File.Delete(path);
        return default;
    }

    static readonly string directorySeparator = Path.DirectorySeparatorChar.ToString();

    public string DirectorySeparator => directorySeparator;

    public string GetTempFileName()
    {
        var name = Path.GetTempFileName();
        return byteStrings ? LuaByteString.FromString(name) : name;
    }

    public ValueTask<ILuaStream> OpenTempFileStream(CancellationToken cancellationToken)
    {
        return new(
            new LuaStream(
                LuaFileOpenMode.WriteUpdate,
                File.Open(Path.GetTempFileName(), FileMode.Open, FileAccess.ReadWrite),
                byteStrings
            )
        );
    }
}
