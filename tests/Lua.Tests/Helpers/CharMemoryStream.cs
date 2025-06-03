using Lua.IO;
namespace Lua.Tests.Helpers;

internal sealed class ReadOnlyCharMemoryLuaIOStream(ReadOnlyMemory<char> buffer, Action<ReadOnlyCharMemoryLuaIOStream>? onDispose  =null,object? state =null) : NotSupportedStreamBase
{
    public readonly ReadOnlyMemory<char> Buffer = buffer;
    int position;
    public readonly object? State = state;
    Action<ReadOnlyCharMemoryLuaIOStream>? onDispose = onDispose;

    public static (string Result, int AdvanceCount) ReadLine(ReadOnlySpan<char> remaining)
    {
        int advanceCount;
        var line = remaining.IndexOfAny('\n', '\r');
        if (line == -1)
        {
            line = remaining.Length;
            advanceCount = line;
        }
        else
        {
            if (remaining[line] == '\r' && line + 1 < remaining.Length && remaining[line + 1] == '\n')
            {
                advanceCount = line + 2;
            }
            else
            {
                advanceCount = line + 1;
            }
        }


        return new(remaining[..line].ToString(), advanceCount);
    }
    public override ValueTask<string?> ReadLineAsync(CancellationToken cancellationToken)
    {
        if (position >= Buffer.Length)
        {
            return new(default(string));
        }

        var remaining = Buffer[position..];
        var (line, advanceCount) = ReadLine(remaining.Span);
        position += advanceCount;
        return new(line);
    }

    public override ValueTask<LuaFileContent> ReadToEndAsync(CancellationToken cancellationToken)
    {
        if (position >= Buffer.Length)
        {
            return new(new LuaFileContent(string.Empty));
        }

        var remaining = Buffer[position..];
        position = Buffer.Length;
        return new( new LuaFileContent(remaining.ToString()));
    }

    public override ValueTask<string?> ReadStringAsync(int count, CancellationToken cancellationToken)
    {
        cancellationToken .ThrowIfCancellationRequested();
        if (position >= Buffer.Length)
        {
            return new("");
        }

        var remaining = Buffer[position..];
        if (count > remaining.Length)
        {
            count = remaining.Length;
        }

        var result = remaining.Slice(0, count).ToString();
        position += count;
        return new(result);
    }

    public override void Dispose()
    {
        onDispose?.Invoke(this);
        onDispose = null;
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        unchecked
        {
            position = origin switch
            {
                SeekOrigin.Begin => (int)offset,
                SeekOrigin.Current => position + (int)offset,
                SeekOrigin.End => (int)(Buffer.Length + offset),
                _ => (int)IOThrowHelpers.ThrowArgumentExceptionForSeekOrigin()
            };
        }

        IOThrowHelpers.ValidatePosition(position, Buffer.Length);

        return position;
    }
}