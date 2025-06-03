namespace Lua.IO;

public sealed class ByteArrayData(byte[] bytes)
{
    public ReadOnlySpan<byte> Bytes => new ReadOnlySpan<byte>(bytes);
    
    public LuaFileContent AsLuaFileContent()
    {
        return new LuaFileContent(bytes);
    }
}