using Lua.IO;

namespace Lua;

public enum LuaFileContentType
{
    Unknown = 0,
    Text,
    Binary
}

public readonly struct LuaFileContent
{
    public LuaFileContentType Type => type;

    readonly LuaFileContentType type;
    readonly object referenceValue;

    public LuaFileContent(ReadOnlyMemory<char> memory)
    {
        type = LuaFileContentType.Text;
        referenceValue = memory;
    }

    public LuaFileContent(ReadOnlyMemory<byte> memory)
    {
        type = LuaFileContentType.Text;
        referenceValue = new BinaryData(memory);
    }

    public LuaFileContent(IBinaryData data)
    {
        type = LuaFileContentType.Binary;
        referenceValue = data;
    }

    public LuaFileContent(string text)
    {
        type = LuaFileContentType.Text;
        referenceValue = text;
    }

    public ReadOnlyMemory<char> ReadText()
    {
        if (type != LuaFileContentType.Text) throw new InvalidOperationException("Cannot read text from a LuaFileContent of type Bytes.");
        if (referenceValue is string str) return str.AsMemory();
        return ((ReadOnlyMemory<char>)referenceValue);
    }

    public string ReadString()
    {
        if (type != LuaFileContentType.Text) throw new InvalidOperationException("Cannot read text from a LuaFileContent of type Bytes.");
        if (referenceValue is string str) return str;
        return ((ReadOnlyMemory<char>)referenceValue).ToString();
    }

    public ReadOnlyMemory<byte> ReadBytes()
    {
        if (type != LuaFileContentType.Binary) throw new InvalidOperationException("Cannot read bytes from a LuaFileContent of type Text.");
        return ((IBinaryData)referenceValue).Memory;
    }

    public LuaValue ToLuaValue()
    {
        if (type == LuaFileContentType.Binary)
        {
            return LuaValue.FromObject(referenceValue);
        }
        else
        {
            return ReadString();
        }
    }
}