using Lua.Internal;
using System.Text;

namespace Lua.IO;

public interface ILuaFileSystem
{
    public bool IsReadable(string path);
    public ILuaIOStream Open(string path, LuaFileMode mode);
    public void Rename(string oldName, string newName);
    public void Remove(string path);
    public string DirectorySeparator { get; }
    public string GetTempFileName();
    public ILuaIOStream OpenTempFileStream();
}

public interface ILuaIOStream : IDisposable
{
    public LuaFileOpenMode Mode { get; }

    public LuaFileContentType ContentType { get; }
    public ValueTask<LuaFileContent> ReadAllAsync(CancellationToken cancellationToken);
    public ValueTask<string?> ReadLineAsync(CancellationToken cancellationToken);
    public ValueTask<string?> ReadStringAsync(int count, CancellationToken cancellationToken);
    public ValueTask WriteAsync(LuaFileContent content, CancellationToken cancellationToken);
    public ValueTask FlushAsync(CancellationToken cancellationToken);
    public void SetVBuf(LuaFileBufferingMode mode, int size);
    public long Seek(long offset, SeekOrigin origin);

    public static ILuaIOStream CreateStreamWrapper(Stream stream, LuaFileOpenMode mode, LuaFileContentType contentType = LuaFileContentType.Text)
    {
        return contentType == LuaFileContentType.Binary
            ? new BinaryLuaIOStream(mode, stream)
            : new TextLuaIOStream(mode, stream);
    }

    public void Close()
    {
        Dispose();
    }
}

public sealed class FileSystem : ILuaFileSystem
{
    public static readonly FileSystem Instance = new();

    public static (FileMode, FileAccess access) GetFileMode(LuaFileOpenMode luaFileOpenMode)
    {
        return luaFileOpenMode switch
        {
            LuaFileOpenMode.Read => (FileMode.Open, FileAccess.Read),
            LuaFileOpenMode.Write => (FileMode.Create, FileAccess.Write),
            LuaFileOpenMode.Append => (FileMode.Append, FileAccess.Write),
            LuaFileOpenMode.ReadWriteOpen => (FileMode.Open, FileAccess.ReadWrite),
            LuaFileOpenMode.ReadWriteCreate => (FileMode.Truncate, FileAccess.ReadWrite),
            LuaFileOpenMode.ReadAppend => (FileMode.Append, FileAccess.ReadWrite),
            _ => throw new ArgumentOutOfRangeException(nameof(luaFileOpenMode), luaFileOpenMode, null)
        };
    }

    public bool IsReadable(string path)
    {
        if (!File.Exists(path)) return false;
        try
        {
            File.Open(path, FileMode.Open, FileAccess.Read).Dispose();
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }


    ILuaIOStream Open(string path, LuaFileOpenMode luaMode, LuaFileContentType contentType)
    {
        var (mode, access) = GetFileMode(luaMode);
        Stream stream;

        if (luaMode == LuaFileOpenMode.ReadAppend)
        {
            stream = File.Open(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.ReadWrite | FileShare.Delete);
        }
        else
        {
            stream = File.Open(path, mode, access, FileShare.ReadWrite | FileShare.Delete);
        }

        ILuaIOStream wrapper = contentType == LuaFileContentType.Binary
            ? new BinaryLuaIOStream(luaMode, stream)
            : new TextLuaIOStream(luaMode, stream);

        if (luaMode == LuaFileOpenMode.ReadAppend)
        {
            wrapper.Seek(0, SeekOrigin.End);
        }

        return wrapper;
    }

    public ILuaIOStream Open(string path, LuaFileMode mode)
    {
        if (mode is LuaFileMode.ReadBinaryOrText)
        {
            return new LuaChunkStream(File.OpenRead(path));
        }

        var openMode = mode.GetOpenMode();
        var contentType = mode.GetContentType();
        return Open(path, openMode, contentType);
    }

    public ILuaIOStream Open(string path, string mode)
    {
        var flags = LuaFileModeExtensions.ParseModeString(mode);
        return Open(path, flags);
    }

    public void Rename(string oldName, string newName)
    {
        if (oldName == newName) return;
        File.Move(oldName, newName);
        File.Delete(oldName);
    }

    public void Remove(string path)
    {
        File.Delete(path);
    }

    static readonly string directorySeparator = Path.DirectorySeparatorChar.ToString();
    public string DirectorySeparator => directorySeparator;

    public string GetTempFileName()
    {
        return Path.GetTempFileName();
    }

    public ILuaIOStream OpenTempFileStream()
    {
        return new TextLuaIOStream(LuaFileOpenMode.ReadAppend, File.Open(Path.GetTempFileName(), FileMode.Open, FileAccess.ReadWrite));
    }
}