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

        if (state.CallStackFrameCount == 0)
        {
            upValues.Add(state.GlobalState.EnvUpValue);
            return;
        }

        var baseIndex = state.GetCallStackFrames()[^1].Base;

        // add upvalues
        for (var i = 0; i < proto.UpValues.Length; i++)
        {
            var description = proto.UpValues[i];
            var upValue = GetUpValueFromDescription(state.GlobalState, state, description, baseIndex);
            upValues.Add(upValue);
        }
    }

    public Prototype Proto { get; }

    public ReadOnlySpan<UpValue> UpValues => upValues.AsSpan();

    internal Span<UpValue> GetUpValuesSpan()
    {
        return upValues.AsSpan();
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

    static UpValue GetUpValueFromDescription(LuaGlobalState globalState, LuaState state, UpValueDesc description, int baseIndex = 0)
    {
        if (description.IsLocal)
        {
            if (description is { Index: 0, Name: "_ENV" })
            {
                return globalState.EnvUpValue;
            }

            return state.GetOrAddUpValue(baseIndex + description.Index);
        }


        if (state.GetCurrentFrame().Function is LuaClosure parentClosure)
        {
            return parentClosure.UpValues[description.Index];
        }

        throw new();
    }
}