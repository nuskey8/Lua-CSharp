namespace Lua.IO;

public class CharMemoryStream(ReadOnlyMemory<char> contents) : ILuaStream
{
    protected int Position;
    bool disposed;

    public LuaFileOpenMode Mode => LuaFileOpenMode.Read;

    public bool IsOpen => !disposed;

    public void Dispose()
    {
        disposed = true;
    }

    public virtual ValueTask<string> ReadAllAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();

        cancellationToken.ThrowIfCancellationRequested();
        if (Position >= contents.Length)
        {
            return new("");
        }

        var remaining = contents[Position..];
        Position = contents.Length;
        return new(remaining.ToString());
    }

    public ValueTask<string?> ReadLineAsync(bool keepEol, CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();

        if (Position >= contents.Length)
        {
            return new((string?)null);
        }

        var remainingSpan = contents[Position..].Span;
        var newlineIndex = remainingSpan.IndexOfAny('\r', '\n');

        string result;
        if (newlineIndex == -1)
        {
            // Read to end
            result = remainingSpan.ToString();
            Position = contents.Length;
        }
        else
        {
            var lineSpan = remainingSpan[..newlineIndex];
            var nlChar = remainingSpan[newlineIndex];
            var endOfLineLength = 1;

            // Check for CRLF
            if (nlChar == '\r' && newlineIndex + 1 < remainingSpan.Length && remainingSpan[newlineIndex + 1] == '\n')
            {
                endOfLineLength = 2; // \r\n
            }

            if (keepEol)
            {
                // Include the newline character(s)
                result = remainingSpan[..(newlineIndex + endOfLineLength)].ToString();
            }
            else
            {
                // Just the line content without newlines
                result = lineSpan.ToString();
            }

            Position += newlineIndex + endOfLineLength;
        }

        return new(result);
    }

    public ValueTask<string?> ReadAsync(int count, CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();

        if (Position >= contents.Length)
        {
            return new((string?)null);
        }

        var available = contents.Length - Position;
        var toRead = Math.Min(count, available);

        var result = contents.Slice(Position, toRead).ToString();
        Position += toRead;

        return new(result);
    }

    public ValueTask<double?> ReadNumberAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();

        if (Position >= contents.Length)
        {
            return new((double?)null);
        }

        var remaining = contents[Position..].Span;
        var startPos = Position;

        // Use the shared utility to scan for a number
        var numberLength = NumberReaderHelper.ScanNumberLength(remaining, true);

        if (numberLength == 0)
        {
            Position = contents.Length;
            return new((double?)null);
        }

        // Find where the actual number starts (after whitespace)
        var whitespaceLength = 0;
        while (whitespaceLength < remaining.Length && char.IsWhiteSpace(remaining[whitespaceLength]))
        {
            whitespaceLength++;
        }

        var numberSpan = remaining.Slice(whitespaceLength, numberLength);
        Position = startPos + whitespaceLength + numberLength;

        // Parse using shared utility
        var result = NumberReaderHelper.ParseNumber(numberSpan);
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

    public ValueTask CloseAsync()
    {
        Dispose();
        return default;
    }

    public long Seek(SeekOrigin origin, long offset)
    {
        ThrowIfDisposed();

        var newPosition = origin switch
        {
            SeekOrigin.Begin => offset,
            SeekOrigin.Current => Position + offset,
            SeekOrigin.End => contents.Length + offset,
            _ => throw new ArgumentException("Invalid seek origin", nameof(origin))
        };

        if (newPosition < 0 || newPosition > contents.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(offset), "Seek position is out of range");
        }

        Position = (int)newPosition;
        return Position;
    }

    protected void ThrowIfDisposed()
    {
        if (disposed)
        {
            throw new ObjectDisposedException(nameof(StringStream));
        }
    }
}

public sealed class StringStream(string content) : CharMemoryStream(content.AsMemory())
{
    public override ValueTask<string> ReadAllAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();
        if (Position == 0)
        {
            return new(content);
        }

        return base.ReadAllAsync(cancellationToken);
    }
}