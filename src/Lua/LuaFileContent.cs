using System.Buffers;

namespace Lua;

public enum LuaFileContentType
{
    Text,
    Bytes
}

public readonly struct LuaFileContent : IDisposable
{
    public LuaFileContentType Type => type;

    readonly LuaFileContentType type;
    readonly object referenceValue;

    public LuaFileContent(string text)
    {
        type = LuaFileContentType.Text;
        referenceValue = text;
    }

    public LuaFileContent(byte[] bytes)
    {
        type = LuaFileContentType.Bytes;
        referenceValue = bytes;
    }

    public LuaFileContent(IMemoryOwner<char> bytes)
    {
        type = LuaFileContentType.Text;
        referenceValue = bytes;
    }

    public LuaFileContent(IMemoryOwner<byte> bytes)
    {
        type = LuaFileContentType.Bytes;
        referenceValue = bytes;
    }

    public ReadOnlySpan<char> ReadText()
    {
        if (type != LuaFileContentType.Text) throw new InvalidOperationException("Cannot read text from a LuaFileContent of type Bytes.");
        if (referenceValue is IMemoryOwner<char> mem)
        {
            return mem.Memory.Span;
        }

        return ((string)referenceValue);
    }

    public ReadOnlySpan<byte> ReadBytes()
    {
        if (type != LuaFileContentType.Bytes) throw new InvalidOperationException("Cannot read bytes from a LuaFileContent of type Text.");
        if (referenceValue is IMemoryOwner<byte> mem)
        {
            return mem.Memory.Span;
        }

        return (byte[])referenceValue;
    }

    public void Dispose()
    {
        if (referenceValue is IDisposable memoryOwner)
        {
            memoryOwner.Dispose();
        }
    }
}