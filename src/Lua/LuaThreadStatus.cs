namespace Lua;

public enum LuaThreadStatus : byte
{
    Suspended,
    Normal,
    Running,
    Dead
}