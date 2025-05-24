using Lua.Internal;
using System.Text;

namespace Lua.IO;

public interface ILuaFileSystem
{
    public bool IsReadable(string path);
    public ValueTask<LuaFileContent> ReadFileContentAsync(string path, CancellationToken cancellationToken);
    public ILuaIOStream Open(string path, LuaFileOpenMode mode);
    public void Rename(string oldName, string newName);
    public void Remove(string path);
    public string DirectorySeparator { get; }
    public string GetTempFileName();
    public ILuaIOStream OpenTempFileStream();
}

public interface ILuaIOStream : IDisposable
{
    public LuaFileOpenMode Mode { get; }
    public ValueTask<string?> ReadLineAsync(CancellationToken cancellationToken);
    public ValueTask<string> ReadToEndAsync(CancellationToken cancellationToken);
    public ValueTask<string?> ReadStringAsync(int count, CancellationToken cancellationToken);
    public ValueTask WriteAsync(ReadOnlyMemory<char> buffer, CancellationToken cancellationToken);
    public ValueTask FlushAsync(CancellationToken cancellationToken);
    public void SetVBuf(LuaFileBufferingMode mode, int size);
    public long Seek(long offset, SeekOrigin origin);

    public static ILuaIOStream CreateStreamWrapper(LuaFileOpenMode mode, Stream stream)
    {
        return new LuaIOStreamWrapper(mode, stream);
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

    public ValueTask<LuaFileContent> ReadFileContentAsync(string path, CancellationToken cancellationToken)
    {
        var bytes = File.ReadAllBytes(path);
        return new(new LuaFileContent(bytes));
    }

    public ILuaIOStream Open(string path, LuaFileOpenMode luaMode)
    {
        var (mode, access) = GetFileMode(luaMode);

        if (luaMode == LuaFileOpenMode.ReadAppend)
        {
            var s = new LuaIOStreamWrapper(luaMode, File.Open(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.ReadWrite | FileShare.Delete));
            s.Seek(0, SeekOrigin.End);
            return s;
        }

        return new LuaIOStreamWrapper(luaMode, File.Open(path, mode, access, FileShare.ReadWrite | FileShare.Delete));
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
        return new LuaIOStreamWrapper(LuaFileOpenMode.ReadAppend, File.Open(Path.GetTempFileName(), FileMode.Open, FileAccess.ReadWrite));
    }
}

internal sealed class LuaIOStreamWrapper(LuaFileOpenMode mode, Stream innerStream) : ILuaIOStream
{
    public LuaFileOpenMode Mode => mode;
    Utf8Reader? reader;
    ulong flushSize = ulong.MaxValue;
    ulong nextFlushSize = ulong.MaxValue;

    public ValueTask<string?> ReadLineAsync(CancellationToken cancellationToken)
    {
        ThrowIfNotReadable();
        reader ??= new();
        return new(reader.ReadLine(innerStream));
    }

    public ValueTask<string> ReadToEndAsync(CancellationToken cancellationToken)
    {
        ThrowIfNotReadable();
        reader ??= new();
        return new(reader.ReadToEnd(innerStream));
    }

    public ValueTask<string?> ReadStringAsync(int count, CancellationToken cancellationToken)
    {
        ThrowIfNotReadable();
        reader ??= new();
        return new(reader.Read(innerStream, count));
    }

    public ValueTask WriteAsync(ReadOnlyMemory<char> buffer, CancellationToken cancellationToken)
    {
        ThrowIfNotWritable();
        if (mode is LuaFileOpenMode.Append or LuaFileOpenMode.ReadAppend)
        {
            innerStream.Seek(0, SeekOrigin.End);
        }

        using var byteBuffer = new PooledArray<byte>(4096);
        var encoder = Encoding.UTF8.GetEncoder();
        var totalBytes = encoder.GetByteCount(buffer.Span, true);
        var remainingBytes = totalBytes;
        while (0 < remainingBytes)
        {
            var byteCount = encoder.GetBytes(buffer.Span, byteBuffer.AsSpan(), false);
            innerStream.Write(byteBuffer.AsSpan()[..byteCount]);
            remainingBytes -= byteCount;
        }

        if (nextFlushSize < (ulong)totalBytes)
        {
            innerStream.Flush();
            nextFlushSize = flushSize;
        }

        reader?.Clear();
        return new();
    }

    public ValueTask FlushAsync(CancellationToken cancellationToken)
    {
        innerStream.Flush();
        nextFlushSize = flushSize;
        return new();
    }

    public void SetVBuf(LuaFileBufferingMode mode, int size)
    {
        // Ignore size parameter
        if (mode is LuaFileBufferingMode.NoBuffering or LuaFileBufferingMode.LineBuffering)
        {
            nextFlushSize = 0;
            flushSize = 0;
        }
        else
        {
            nextFlushSize = (ulong)size;
            flushSize = (ulong)size;
        }
    }

    public long Seek(long offset, SeekOrigin origin)
    {
        reader?.Clear();
        return innerStream.Seek(offset, origin);
    }

    public bool CanRead => innerStream.CanRead;
    public bool CanSeek => innerStream.CanSeek;
    public bool CanWrite => innerStream.CanWrite;

    void ThrowIfNotReadable()
    {
        if (!innerStream.CanRead)
        {
            throw new IOException("Stream is not readable.");
        }
    }

    void ThrowIfNotWritable()
    {
        if (!innerStream.CanWrite)
        {
            throw new IOException("Stream is not writable.");
        }
    }

    public void Dispose()
    {
        if (innerStream.CanWrite) innerStream.Flush();
        innerStream.Dispose();
        reader?.Dispose();
    }
}