namespace Lua.IO;

public sealed class FileSystem(string? baseDirectory = null) : ILuaFileSystem
{
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

    public bool IsReadable(string path)
    {
        return File.Exists(GetFullPath(path));
    }

    public ValueTask<ILuaStream> Open(
        string path,
        LuaFileOpenMode openMode,
        CancellationToken cancellationToken
    )
    {
        var (mode, access) = GetFileMode(openMode);
        Stream stream;
        path = GetFullPath(path);
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

        ILuaStream wrapper = new LuaStream(openMode, stream);

        if (openMode == LuaFileOpenMode.AppendUpdate)
        {
            wrapper.Seek(SeekOrigin.End, 0);
        }

        return new(wrapper);
    }

    public ValueTask Rename(string oldName, string newName, CancellationToken cancellationToken)
    {
        oldName = GetFullPath(oldName);
        newName = GetFullPath(newName);

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
        path = GetFullPath(path);
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
        return Path.GetTempFileName();
    }

    public ValueTask<ILuaStream> OpenTempFileStream(CancellationToken cancellationToken)
    {
        return new(
            new LuaStream(
                LuaFileOpenMode.WriteUpdate,
                File.Open(Path.GetTempFileName(), FileMode.Open, FileAccess.ReadWrite)
            )
        );
    }
}
