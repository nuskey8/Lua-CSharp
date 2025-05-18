namespace Lua;

public interface ILuaFileManager
{
    public bool IsReadable(string path);
    public ValueTask<LuaFileContent> ReadFileContentAsync(string path, CancellationToken cancellationToken);
    public IStream Open(string path, FileMode mode, FileAccess access);
    public void Rename (string oldName, string newName);
    public void Remove (string path);
}


public interface IStream : IDisposable
{
    public IStreamReader? Reader { get; }
    public IStreamWriter? Writer { get; }

    public long Seek(long offset, SeekOrigin origin);

    public void SetLength(long value);

    public bool CanRead { get; }
    public bool CanSeek { get; }
    public bool CanWrite { get; }
    public long Length { get; }
    public long Position { get; set; }
}

public interface IStreamReader : IDisposable
{
    public ValueTask<string?> ReadLineAsync(CancellationToken cancellationToken);
    public ValueTask<string> ReadToEndAsync(CancellationToken cancellationToken);
    public ValueTask<int> ReadByteAsync(CancellationToken cancellationToken);
}

public interface IStreamWriter : IDisposable
{
    public ValueTask WriteAsync(ReadOnlyMemory<char> buffer, CancellationToken cancellationToken);
    public ValueTask FlushAsync(CancellationToken cancellationToken);

    public void SetVBuf(string mode, int size);
}

public sealed class SystemFileManager : ILuaFileManager
{
    public static readonly SystemFileManager Instance = new();

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

    public IStream Open(string path, FileMode mode, FileAccess access)
    {
        return new SystemStream(File.Open(path, mode, access));
    }

    public ValueTask WriteAllTextAsync(string path, string text, CancellationToken cancellationToken)
    {
        File.WriteAllText(path, text);
        return new();
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
}

public sealed class SystemStreamReader(StreamReader streamReader) : IStreamReader
{
    public ValueTask<string?> ReadLineAsync(CancellationToken cancellationToken)
    {
        return new(streamReader.ReadLine());
    }

    public ValueTask<string> ReadToEndAsync(CancellationToken cancellationToken)
    {
        return new(streamReader.ReadToEnd());
    }

    public ValueTask<int> ReadByteAsync(CancellationToken cancellationToken)
    {
        return new(streamReader.Read());
    }

    public void Dispose()
    {
        streamReader.Dispose();
    }
}

public sealed class SystemStreamWriter(StreamWriter streamWriter) : IStreamWriter
{
    public ValueTask WriteAsync(ReadOnlyMemory<char> buffer, CancellationToken cancellationToken)
    {
        streamWriter.Write(buffer.Span);
        return new();
    }

    public ValueTask WriteLineAsync(ReadOnlyMemory<char> buffer, CancellationToken cancellationToken)
    {
        streamWriter.WriteLine(buffer.Span);
        return new();
    }

    public ValueTask FlushAsync(CancellationToken cancellationToken)
    {
        streamWriter.Flush();
        return new();
    }

    public void SetVBuf(string mode, int size)
    {
        // Ignore size parameter
        streamWriter.AutoFlush = mode is "no" or "line";
    }

    public void Dispose()
    {
        streamWriter.Dispose();
    }
}

public sealed class SystemStream(Stream fileStream) : IStream
{
    public IStreamReader? Reader => fileStream.CanRead ? new SystemStreamReader(new(fileStream)) : null;
    public IStreamWriter? Writer => fileStream.CanWrite ? new SystemStreamWriter(new(fileStream)) : null;

    public long Seek(long offset, SeekOrigin origin) => fileStream.Seek(offset, origin);

    public void SetLength(long value) => fileStream.SetLength(value);

    public bool CanRead => fileStream.CanRead;
    public bool CanSeek => fileStream.CanSeek;
    public bool CanWrite => fileStream.CanWrite;
    public long Length => fileStream.Length;

    public long Position
    {
        get => fileStream.Position;
        set => fileStream.Position = value;
    }

    public void Dispose() => fileStream.Dispose();
}