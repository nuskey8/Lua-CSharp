namespace Lua.IO;

public class CharMemoryStream(ReadOnlyMemory<char> contents) : ILuaStream
{
    protected int Position;
    private bool disposed;

    public LuaFileOpenMode Mode => LuaFileOpenMode.Read;

    public void Dispose()
    {
        disposed = true;
    }

    public virtual ValueTask<string> ReadAllAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();

        cancellationToken.ThrowIfCancellationRequested();
        if (Position >= contents.Length)
            return new("");

        var remaining = contents[Position..];
        Position = contents.Length;
        return new(remaining.ToString());
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

    public ValueTask WriteAsync(ReadOnlyMemory<char> content, CancellationToken cancellationToken)
    {
        throw new IOException("Stream is read-only");
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
    public override ValueTask<string> ReadAllAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();
        if (Position == 0)
            return new((content));
        return base.ReadAllAsync(cancellationToken);
    }
}