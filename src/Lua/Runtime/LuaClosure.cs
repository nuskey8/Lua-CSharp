using System.Runtime.CompilerServices;

namespace Lua.Runtime;

public sealed class LuaClosure : LuaFunction
{
    readonly UpValue[] upValues;

    LuaClosure(Prototype proto)
        : base(
            proto.ChunkName,
            static (context, ct) => LuaVirtualMachine.ExecuteClosureAsync(context.State, ct)
        )
    {
        Proto = proto;
        upValues = proto.UpValues.IsEmpty ? [] : new UpValue[proto.UpValues.Length];
    }

    /// <summary>
    /// Creates a loaded chunk with its first upvalue set to the environment
    /// and any remaining upvalues set to nil.
    /// </summary>
    public LuaClosure(LuaState state, Prototype proto, LuaTable? environment = null)
        : this(proto)
    {
        if (proto.UpValues.IsEmpty)
            return;

        upValues[0] = UpValue.Closed(environment ?? state.Environment);
        for (var i = 1; i < proto.UpValues.Length; i++)
            upValues[i] = UpValue.Closed(LuaValue.Nil);
    }

    internal LuaClosure(LuaState state, Prototype proto, LuaClosure parent, int frameBase)
        : this(proto)
    {
        var descriptions = proto.UpValues;
        for (var i = 0; i < descriptions.Length; i++)
        {
            var description = descriptions[i];
            upValues[i] = description.IsLocal
                ? state.GetOrAddUpValue(frameBase + description.Index)
                : parent.upValues[description.Index];
        }
    }

    public Prototype Proto { get; }

    public ReadOnlySpan<UpValue> UpValues => upValues;

    internal Span<UpValue> GetUpValuesSpan()
    {
        return upValues;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal LuaValue GetUpValue(int index)
    {
        return upValues[index].GetValue();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal ref readonly LuaValue GetUpValueRef(int index)
    {
        return ref upValues[index].GetValueRef();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal void SetUpValue(int index, LuaValue value)
    {
        upValues[index].SetValue(value);
    }

    internal void SetEnvironment(LuaValue environment)
    {
        if (upValues.Length != 0)
            upValues[0].SetValue(environment);
    }
}
