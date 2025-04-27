using Lua.CodeAnalysis;
using System.Runtime.CompilerServices;
using Lua.Internal;

namespace Lua.Runtime;

public sealed class LuaClosure : LuaFunction
{
    FastListCore<UpValue> upValues;

    public LuaClosure(LuaState state, Prototype proto, LuaTable? environment = null)
        : base(proto.ChunkName, static (context, ct) => LuaVirtualMachine.ExecuteClosureAsync(context.State, ct))
    {
        Proto = proto;
        if (environment != null)
        {
            upValues.Add(UpValue.Closed(environment));
            return;
        }

        if (state.CurrentThread.CallStack.Count == 0)
        {
            upValues.Add(state.EnvUpValue);
            return;
        }

        var baseIndex = state.CurrentThread.CallStack.Peek().Base;

        // add upvalues
        for (int i = 0; i < proto.UpValues.Length; i++)
        {
            var description = proto.UpValues[i];
            var upValue = GetUpValueFromDescription(state, state.CurrentThread, description, baseIndex);
            upValues.Add(upValue);
        }
    }

    public Prototype Proto { get; }

    public ReadOnlySpan<UpValue> UpValues => upValues.AsSpan();
    internal Span<UpValue> GetUpValuesSpan() => upValues.AsSpan();

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

    static UpValue GetUpValueFromDescription(LuaState state, LuaThread thread, UpValueDesc description, int baseIndex = 0)
    {
        if (description.IsLocal)
        {
            return state.GetOrAddUpValue(thread, baseIndex + description.Index);
        }


        if (thread.GetCurrentFrame().Function is LuaClosure parentClosure)
        {
            return parentClosure.UpValues[description.Index];
        }

        throw new Exception();
    }
}