using Lua.IO;
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
    private readonly int length = -1;
    readonly object referenceValue;

    public LuaFileContent(string text, int length = -1)
    {
        type = LuaFileContentType.Text;
        if (length == -1)
            length = text.Length;


        this.length = length;

        referenceValue = text;
    }

    public LuaFileContent(char[] text, int length = -1)
    {
        type = LuaFileContentType.Text;
        if (length == -1)
            length = text.Length;

        this.length = length;
        referenceValue = text;
    }

    public LuaFileContent(byte[] bytes, int length = -1)
    {
        type = LuaFileContentType.Bytes;
        referenceValue = bytes;
        if (length == -1)
            length = bytes.Length;
        this.length = length;
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

    public ReadOnlyMemory<char> ReadText()
    {
        if (type != LuaFileContentType.Text) throw new InvalidOperationException("Cannot read text from a LuaFileContent of type Bytes.");
        if (referenceValue is IMemoryOwner<char> mem)
        {
            return mem.Memory;
        }

        if (referenceValue is char[] chars)
        {
            return chars.AsMemory(0, length);
        }

        return ((string)referenceValue).AsMemory(0, length);
    }
    
    public string ReadString()
    {
        if (type != LuaFileContentType.Text) throw new InvalidOperationException("Cannot read text from a LuaFileContent of type Bytes.");
        if (referenceValue is string str && length == str.Length)
        {
            return (str);
        }
        if (referenceValue is IMemoryOwner<char> mem)
        {
            return mem.Memory.Span.ToString();
        }

        if (referenceValue is char[] chars)
        {
            return chars.AsSpan(0, length).ToString();
        }

        return ((string)referenceValue).Substring(0, length);
    }

    public ReadOnlyMemory<byte> ReadBytes()
    {
        if (type != LuaFileContentType.Bytes) throw new InvalidOperationException("Cannot read bytes from a LuaFileContent of type Text.");
        if (referenceValue is IMemoryOwner<byte> mem)
        {
            return mem.Memory;
        }

        return ((byte[])referenceValue).AsMemory(0, length);
    }

    public void Dispose()
    {
        if (referenceValue is IDisposable memoryOwner)
        {
            memoryOwner.Dispose();
        }
    }

    public LuaValue ToLuaValue()
    {
        if (referenceValue is string str && length == str.Length)
        {
            return (str);
        }

        using (this)
        {
            if (type == LuaFileContentType.Bytes)
            {
                return LuaValue.FromObject(new ByteArrayData(ReadBytes().ToArray()));
            }
            else
            {
                return ReadText().Span.ToString();
            }
        }
    }
}