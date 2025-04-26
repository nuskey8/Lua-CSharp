using System.Numerics;
using Lua;

[LuaObject]
public partial class LVec3
{
    Vector3 value;

    [LuaMember("x")]
    public float X
    {
        get => value.X;
        set => this.value = this.value with { X = value };
    }

    [LuaMember("y")]
    public float Y
    {
        get => value.Y;
        set => this.value = this.value with { Y = value };
    }

    [LuaMember("z")]
    public float Z
    {
        get => value.Z;
        set => this.value = this.value with { Z = value };
    }

    [LuaMember("create")]
    public static LVec3 Create(float x, float y, float z)
    {
        return new LVec3() { value = new Vector3(x, y, z) };
    }

    public override string ToString()
    {
        return value.ToString();
    }
}