using System.Buffers;

namespace Lua;

public enum LuaModuleType
{
    Text,
    Bytes
}

public readonly struct LuaModule : IDisposable
{
    public string Name => name;
    public LuaModuleType Type => type;

    readonly string name;
    readonly LuaModuleType type;
    readonly object referenceValue;

    public LuaModule(string name, string text)
    {
        this.name = name;
        type = LuaModuleType.Text;
        referenceValue = text;
    }

    public LuaModule(string name, byte[] bytes)
    {
        this.name = name;
        type = LuaModuleType.Bytes;
        referenceValue = bytes;
    }

    public LuaModule(string name, IMemoryOwner<char> bytes)
    {
        this.name = name;
        type = LuaModuleType.Text;
        referenceValue = bytes;
    }

    public LuaModule(string name, IMemoryOwner<byte> bytes)
    {
        this.name = name;
        type = LuaModuleType.Bytes;
        referenceValue = bytes;
    }

    public ReadOnlySpan<char> ReadText()
    {
        if (type != LuaModuleType.Text) throw new Exception(); // TODO: add message
        if (referenceValue is IMemoryOwner<char> mem)
        {
            return mem.Memory.Span;
        }

        return ((string)referenceValue);
    }

    public ReadOnlySpan<byte> ReadBytes()
    {
        if (type != LuaModuleType.Bytes) throw new Exception(); // TODO: add message
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