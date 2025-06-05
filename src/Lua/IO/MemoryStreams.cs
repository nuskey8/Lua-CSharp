namespace Lua.IO;

public sealed class ByteMemoryStream(ReadOnlyMemory<byte> bytes) : ILuaIOStream
{
    private int position;
    private bool disposed;

    public ByteMemoryStream(byte[] bytes) : this(bytes.AsMemory())
    {
        if (bytes is null)
            throw new ArgumentNullException(nameof(bytes));
    }

    public LuaFileOpenMode Mode => LuaFileOpenMode.Read;

    public LuaFileContentType ContentType => LuaFileContentType.Binary;

    public void Dispose()
    {
        disposed = true;
    }

    public ValueTask<LuaFileContent> ReadAllAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();

        var remaining = bytes.Slice(position);
        position = bytes.Length;
        return new(new LuaFileContent(remaining));
    }

    public ValueTask<string?> ReadLineAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();

        throw new InvalidOperationException("Cannot read lines string from a binary stream.");
    }

    public ValueTask<string?> ReadStringAsync(int count, CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();
        throw new InvalidOperationException("Cannot read lines string from a binary stream.");
    }

    public ValueTask WriteAsync(LuaFileContent content, CancellationToken cancellationToken)
    {
        throw new NotSupportedException("Stream is read-only");
    }

    public ValueTask FlushAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        return default;
    }

    public void SetVBuf(LuaFileBufferingMode mode, int size)
    {
        // No-op for memory streams
    }

    public long Seek(long offset, SeekOrigin origin)
    {
        ThrowIfDisposed();

        long newPosition = origin switch
        {
            SeekOrigin.Begin => offset,
            SeekOrigin.Current => position + offset,
            SeekOrigin.End => bytes.Length + offset,
            _ => throw new ArgumentException("Invalid seek origin", nameof(origin))
        };

        if (newPosition < 0 || newPosition > bytes.Length)
            throw new ArgumentOutOfRangeException(nameof(offset), "Seek position is out of range");

        position = (int)newPosition;
        return position;
    }

    private void ThrowIfDisposed()
    {
        if (disposed)
            throw new ObjectDisposedException(nameof(ByteMemoryStream));
    }
}

public class CharMemoryStream(ReadOnlyMemory<char> contents) : ILuaIOStream
{
    protected int Position;
    private bool disposed;

    public LuaFileOpenMode Mode => LuaFileOpenMode.Read;

    public LuaFileContentType ContentType => LuaFileContentType.Text;

    public void Dispose()
    {
        disposed = true;
    }

    public virtual ValueTask<LuaFileContent> ReadAllAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();

        cancellationToken.ThrowIfCancellationRequested();
        if (Position >= contents.Length)
            return new(new LuaFileContent(""));

        var remaining = contents[Position..];
        Position = contents.Length;
        return new(new LuaFileContent(remaining));
    }

    public ValueTask<string?> ReadLineAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();

        if (Position >= contents.Length)
            return new((string?)null);

        var remainingSpan = contents.Slice(Position).Span;
        var newlineIndex = remainingSpan.IndexOf('\n');

        string result;
        if (newlineIndex == -1)
        {
            // Read to end
            result = remainingSpan.ToString();
            Position = contents.Length;
        }
        else
        {
            // Read up to newline
            var lineSpan = remainingSpan[..newlineIndex];
            // Remove CR if present
            if (lineSpan.Length > 0 && lineSpan[^1] == '\r')
                lineSpan = lineSpan[..^1];

            result = lineSpan.ToString();
            Position += newlineIndex + 1;
        }

        return new(result);
    }

    public ValueTask<string?> ReadStringAsync(int count, CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();

        if (Position >= contents.Length)
            return new((string?)null);

        var available = contents.Length - Position;
        var toRead = Math.Min(count, available);

        var result = contents.Slice(Position, toRead).ToString();
        Position += toRead;

        return new(result);
    }

    public ValueTask WriteAsync(LuaFileContent content, CancellationToken cancellationToken)
    {
        throw new NotSupportedException("Stream is read-only");
    }

    public ValueTask FlushAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        return default;
    }

    public void SetVBuf(LuaFileBufferingMode mode, int size)
    {
        // No-op for memory streams
    }

    public long Seek(long offset, SeekOrigin origin)
    {
        ThrowIfDisposed();

        long newPosition = origin switch
        {
            SeekOrigin.Begin => offset,
            SeekOrigin.Current => Position + offset,
            SeekOrigin.End => contents.Length + offset,
            _ => throw new ArgumentException("Invalid seek origin", nameof(origin))
        };

        if (newPosition < 0 || newPosition > contents.Length)
            throw new ArgumentOutOfRangeException(nameof(offset), "Seek position is out of range");

        Position = (int)newPosition;
        return Position;
    }

    protected void ThrowIfDisposed()
    {
        if (disposed)
            throw new ObjectDisposedException(nameof(StringStream));
    }
}

public sealed class StringStream(string content) : CharMemoryStream(content.AsMemory())
{
    public override ValueTask<LuaFileContent> ReadAllAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();
        if (Position == 0)
            return new(new LuaFileContent(content));
        return base.ReadAllAsync(cancellationToken);
    }
}