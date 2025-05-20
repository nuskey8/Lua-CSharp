using Lua.Internal;

namespace Lua.IO;

public interface ILuaFileSystem
{
    public bool IsReadable(string path);
    public ValueTask<LuaFileContent> ReadFileContentAsync(string path, CancellationToken cancellationToken);
    public IStream? Open(string path, LuaFileOpenMode mode, bool throwError);
    public void Rename(string oldName, string newName);
    public void Remove(string path);
    public string DirectorySeparator { get; }
    public string GetTempFileName();
}

public interface IStream : IDisposable
{
    public ValueTask<string?> ReadLineAsync(CancellationToken cancellationToken);
    public ValueTask<string> ReadToEndAsync(CancellationToken cancellationToken);
    public ValueTask<string?> ReadStringAsync(int count, CancellationToken cancellationToken);

    public ValueTask WriteAsync(ReadOnlyMemory<char> buffer, CancellationToken cancellationToken);
    public ValueTask FlushAsync(CancellationToken cancellationToken);
    public void SetVBuf(string mode, int size);

    public long Seek(long offset, SeekOrigin origin);

    public void SetLength(long value);

    public bool CanRead { get; }
    public bool CanSeek { get; }
    public bool CanWrite { get; }
    public long Length { get; }
    public long Position { get; set; }
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
            LuaFileOpenMode.ReadWriteCreate => (FileMode.Create, FileAccess.ReadWrite),
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

    public ValueTask<LuaFileContent> ReadFileContentAsync(string path, CancellationToken cancellationToken)
    {
        var bytes = File.ReadAllBytes(path);
        return new(new LuaFileContent(bytes));
    }

    public IStream? Open(string path, LuaFileOpenMode luaMode, bool throwError)
    {
        var (mode, access) = GetFileMode(luaMode);
        try
        {
            return new StreamWrapper(File.Open(path, mode, access));
        }
        catch (Exception)
        {
            if (throwError)
            {
                throw;
            }

            return null;
        }
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
}

public sealed class StreamWrapper(Stream innerStream) : IStream
{
    StreamReader? reader;
    StreamWriter? writer;

    public ValueTask<string?> ReadLineAsync(CancellationToken cancellationToken)
    {
        reader ??= new(innerStream);

        return new(reader.ReadLine());
    }

    public ValueTask<string> ReadToEndAsync(CancellationToken cancellationToken)
    {
        reader ??= new(innerStream);

        return new(reader.ReadToEnd());
    }

    public ValueTask<string?> ReadStringAsync(int count, CancellationToken cancellationToken)
    {
        reader ??= new(innerStream);

        using var byteBuffer = new PooledArray<char>(count);
        var span = byteBuffer.AsSpan();
        var ret = reader.Read(span);
        if (ret != span.Length)
        {
            return new(default(string));
        }

        return new(span.ToString());
    }

    public ValueTask WriteAsync(ReadOnlyMemory<char> buffer, CancellationToken cancellationToken)
    {
        writer ??= new(innerStream);

        writer.Write(buffer.Span);
        return new();
    }

    public ValueTask FlushAsync(CancellationToken cancellationToken)
    {
        innerStream.Flush();
        return new();
    }

    public void SetVBuf(string mode, int size)
    {
        writer ??= new(innerStream);
        // Ignore size parameter
        writer.AutoFlush = mode is "no" or "line";
    }

    public long Seek(long offset, SeekOrigin origin) => innerStream.Seek(offset, origin);

    public void SetLength(long value) => innerStream.SetLength(value);

    public bool CanRead => innerStream.CanRead;
    public bool CanSeek => innerStream.CanSeek;
    public bool CanWrite => innerStream.CanWrite;
    public long Length => innerStream.Length;

    public long Position
    {
        get => innerStream.Position;
        set => innerStream.Position = value;
    }

    public void Dispose() => innerStream.Dispose();
}