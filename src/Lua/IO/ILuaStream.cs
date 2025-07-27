namespace Lua.IO;

public interface ILuaStream : IDisposable
{
    bool IsOpen { get; }
    LuaFileOpenMode Mode { get; }

    ValueTask<string> ReadAllAsync(CancellationToken cancellationToken)
    {
        Mode.ThrowIfNotReadable();

        // Default implementation using ReadStringAsync
        throw new NotImplementedException($"ReadAllAsync must be implemented by {GetType().Name}");
    }

    ValueTask<double?> ReadNumberAsync(CancellationToken cancellationToken)
    {
        Mode.ThrowIfNotReadable();

        // Default implementation using ReadStringAsync
        throw new NotImplementedException($"ReadNumberAsync must be implemented by {GetType().Name}");
    }

    ValueTask<string?> ReadLineAsync(bool keepEol, CancellationToken cancellationToken)
    {
        Mode.ThrowIfNotReadable();


        // Default implementation using ReadStringAsync
        throw new NotImplementedException($"ReadLineAsync must be implemented by {GetType().Name}");
    }

    ValueTask<string?> ReadAsync(int count, CancellationToken cancellationToken)
    {
        Mode.ThrowIfNotReadable();

        // Default implementation using ReadAllAsync
        throw new NotImplementedException($"ReadStringAsync must be implemented by {GetType().Name}");
    }

    ValueTask WriteAsync(ReadOnlyMemory<char> content, CancellationToken cancellationToken)
    {
        Mode.ThrowIfNotWritable();

        throw new NotImplementedException($"WriteAsync must be implemented by {GetType().Name}");
    }

    ValueTask WriteAsync(string content, CancellationToken cancellationToken)
    {
        return WriteAsync(content.AsMemory(), cancellationToken);
    }

    ValueTask FlushAsync(CancellationToken cancellationToken)
    {
        // Default implementation does nothing (no buffering)
        return default;
    }

    void SetVBuf(LuaFileBufferingMode mode, int size)
    {
        // Default implementation does nothing (no configurable buffering)
    }

    long Seek(SeekOrigin origin, long offset)
    {
        throw new NotSupportedException($"Seek is not supported by {GetType().Name}");
    }

    static ILuaStream CreateFromStream(Stream stream, LuaFileOpenMode openMode)
    {
        return new LuaStream(openMode, stream);
    }

    static ILuaStream CreateFromString(string content)
    {
        return new StringStream(content);
    }

    static ILuaStream CreateFromMemory(ReadOnlyMemory<char> content)
    {
        return new CharMemoryStream(content);
    }


    ValueTask CloseAsync(CancellationToken cancellationToken)
    {
        Dispose();
        return default;
    }
}