using Lua.Internal;

namespace Lua.IO;

public class BufferedOutputStream(Action<ReadOnlyMemory<char>> onFlush) : ILuaStream
{
    public void Dispose()
    {
        IsOpen = false;
    }

    public bool IsOpen { get; set; } = true;
    public LuaFileOpenMode Mode  => LuaFileOpenMode.Write;

    private FastListCore<char> buffer;
    public ValueTask WriteAsync(ReadOnlyMemory<char> text, CancellationToken cancellationToken = default)
    {
        foreach (var c in text.Span)
        {
            buffer .Add(c);
        }
        return default;
    }
        
    public ValueTask FlushAsync(CancellationToken cancellationToken = default)
    {
        if (buffer.Length > 0)
        {
            onFlush(buffer.AsArray().AsMemory(0, buffer.Length));
            buffer.Clear();
        }
        return default;
    }
        
}