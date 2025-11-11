namespace Lua;

public enum LuaModuleType
{
    Text,
    Bytes
}

public readonly struct LuaModule
{
    public string Name => name;

    public LuaModuleType Type => type;

    readonly string name;
    readonly LuaModuleType type;
    readonly ReadOnlyMemory<char> text;
    readonly ReadOnlyMemory<byte> bytes;

    public LuaModule(string name, ReadOnlyMemory<char> text)
    {
        this.name = name;
        type = LuaModuleType.Text;
        this.text = text;
    }

    public LuaModule(string name, ReadOnlyMemory<byte> bytes)
    {
        this.name = name;
        type = LuaModuleType.Bytes;
        this.bytes = bytes;
    }

    public LuaModule(string name, string text) : this(name, text.AsMemory()) { }

    public LuaModule(string name, byte[] bytes)
        : this(name, new ReadOnlyMemory<byte>(bytes))
    {
    }

    public ReadOnlySpan<char> ReadText()
    {
        if (type != LuaModuleType.Text)
        {
            throw new(); // TODO: add message
        }

        return text.Span;
    }

    public ReadOnlySpan<byte> ReadBytes()
    {
        if (type != LuaModuleType.Bytes)
        {
            throw new(); // TODO: add message
        }

        return bytes.Span;
    }
}