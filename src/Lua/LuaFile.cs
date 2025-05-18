using System.Buffers;

namespace Lua;

public enum LuaFileType
{
    Text,
    Bytes
}

public readonly struct LuaFile : IDisposable
{
    public LuaFileType Type => type;

    readonly LuaFileType type;
    readonly object referenceValue;

    public LuaFile(string text)
    {
        type = LuaFileType.Text;
        referenceValue = text;
    }

    public LuaFile(byte[] bytes)
    {
        type = LuaFileType.Bytes;
        referenceValue = bytes;
    }

    public LuaFile(IMemoryOwner<char> bytes)
    {
        type = LuaFileType.Text;
        referenceValue = bytes;
    }

    public LuaFile(IMemoryOwner<byte> bytes)
    {
        type = LuaFileType.Bytes;
        referenceValue = bytes;
    }

    public ReadOnlySpan<char> ReadText()
    {
        if (type != LuaFileType.Text) throw new Exception(); // TODO: add message
        if (referenceValue is IMemoryOwner<char> mem)
        {
            return mem.Memory.Span;
        }

        return ((string)referenceValue);
    }

    public ReadOnlySpan<byte> ReadBytes()
    {
        if (type != LuaFileType.Bytes) throw new Exception(); // TODO: add message
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