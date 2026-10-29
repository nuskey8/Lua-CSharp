using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Lua.Runtime;

namespace Lua;

[StructLayout(LayoutKind.Auto)]
public readonly record struct LuaFunctionExecutionContext
{
    internal LuaGlobalState GlobalState => State.GlobalState;

    public LuaState State { get; init; }
    public required int ArgumentCount { get; init; }
    public required int ReturnFrameBase { get; init; }

    // public object? AdditionalContext { get; init; }

    public int FrameBase => State.Stack.Count - ArgumentCount;

    public ReadOnlySpan<LuaValue> Arguments
    {
        get
        {
            var stack = State.Stack.AsSpan();
            return stack[^ArgumentCount..];
        }
    }

    public ReadOnlyMemory<LuaValue> ArgumentsMemory
    {
        get
        {
            var stack = State.Stack;
            var memory = stack.GetBufferMemory();
            return memory[(stack.Count - ArgumentCount)..stack.Count];
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool HasArgument(int index)
    {
        return ArgumentCount > index && Arguments[index].Type is not LuaValueType.Nil;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public LuaValue GetArgument(int index)
    {
        ThrowIfArgumentNotExists(index);
        return Arguments[index];
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal LuaValue GetArgumentOrDefault(int index, LuaValue defaultValue = default)
    {
        if (ArgumentCount <= index)
        {
            return defaultValue;
        }

        return Arguments[index];
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public T GetArgument<T>(int index)
    {
        ThrowIfArgumentNotExists(index);
        return ReadArgument<T>(index, in Arguments[index]);
    }

    // Lua library strings accept numbers; typed C# arguments remain strict.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal string GetStringArgument(int index)
    {
        var argument = GetArgument(index);
        if (argument.TryReadString(out var text))
            return text;
        if (argument.Type == LuaValueType.Number)
        {
            text = argument.ToLuaString(State.Platform.NumberFormat);
            State.Stack.Get(FrameBase + index) = text;
            return text;
        }
        ThrowArgumentError<string>(index, argument);
        return null!;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal T GetArgumentOrDefault<T>(int index, T defaultValue = default!)
    {
        if (ArgumentCount <= index)
        {
            return defaultValue;
        }

        ref readonly var arg = ref Arguments[index];

        if (arg.Type is LuaValueType.Nil)
        {
            return defaultValue;
        }

        return ReadArgument<T>(index, arg);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    T ReadArgument<T>(int index, in LuaValue arg)
    {
        // Keep number reads inline without expanding generic conversion and
        // error formatting into every standard library function.
        if (typeof(T) == typeof(double))
        {
            if (arg.TryReadDouble(out var number))
            {
                return Unsafe.As<double, T>(ref number);
            }
        }
        else if (arg.TryRead<T>(out var value))
        {
            return value;
        }

        ThrowArgumentError<T>(index, arg);
        return default!;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    void ThrowArgumentError<T>(int index, in LuaValue arg)
    {
        var t = typeof(T);
        if (
            (t == typeof(int) || t == typeof(long) || t == typeof(uint) || t == typeof(ulong))
            && arg.TryReadNumber(out _)
        )
        {
            LuaRuntimeException.BadArgumentNumberIsNotInteger(State, index + 1);
        }
        else if (LuaValue.TryGetLuaValueType(t, out var type))
        {
            LuaRuntimeException.BadArgument(State, index + 1, type, arg.Type);
        }
        else if (arg.Type is LuaValueType.UserData or LuaValueType.LightUserData)
        {
            LuaRuntimeException.BadArgument(
                State,
                index + 1,
                t.Name,
                arg.UnsafeRead<object>()?.GetType().ToString() ?? "userdata: 0"
            );
        }
        else
        {
            LuaRuntimeException.BadArgument(State, index + 1, t.Name, arg.TypeToString());
        }
    }

    public int Return()
    {
        State.Stack.PopUntil(ReturnFrameBase);
        return 0;
    }

    public int Return(LuaValue result)
    {
        var stack = State.Stack;
        stack.SetTop(ReturnFrameBase + 1);
        stack.FastGet(ReturnFrameBase) = result;
        return 1;
    }

    public int Return(LuaValue result0, LuaValue result1)
    {
        var stack = State.Stack;
        stack.SetTop(ReturnFrameBase + 2);
        stack.FastGet(ReturnFrameBase) = result0;
        stack.FastGet(ReturnFrameBase + 1) = result1;
        return 2;
    }

    public int Return(LuaValue result0, LuaValue result1, LuaValue result2)
    {
        var stack = State.Stack;
        stack.SetTop(ReturnFrameBase + 3);
        stack.FastGet(ReturnFrameBase) = result0;
        stack.FastGet(ReturnFrameBase + 1) = result1;
        stack.FastGet(ReturnFrameBase + 2) = result2;
        return 3;
    }

    public int Return(ReadOnlySpan<LuaValue> results)
    {
        var stack = State.Stack;
        stack.EnsureCapacity(ReturnFrameBase + results.Length);
        results.CopyTo(stack.GetBuffer()[ReturnFrameBase..(ReturnFrameBase + results.Length)]);
        stack.SetTop(ReturnFrameBase + results.Length);
        return results.Length;
    }

    internal int Return(LuaValue result0, ReadOnlySpan<LuaValue> results)
    {
        var stack = State.Stack;
        stack.EnsureCapacity(ReturnFrameBase + results.Length);
        stack.SetTop(ReturnFrameBase + results.Length + 1);
        var buffer = stack.GetBuffer();
        buffer[ReturnFrameBase] = result0;
        results.CopyTo(buffer[(ReturnFrameBase + 1)..(ReturnFrameBase + results.Length + 1)]);
        return results.Length + 1;
    }

    public Span<LuaValue> GetReturnBuffer(int count)
    {
        var stack = State.Stack;
        stack.SetTop(ReturnFrameBase + count);
        var buffer = stack.GetBuffer()[ReturnFrameBase..(ReturnFrameBase + count)];
        return buffer;
    }

    public CSharpClosure? GetCsClosure()
    {
        return State.GetCurrentFrame().Function as CSharpClosure;
    }

    internal void ThrowBadArgument(int index, string message)
    {
        LuaRuntimeException.BadArgument(
            State,
            index,
            State.GetCurrentFrame().Function.Name,
            message
        );
    }

    void ThrowIfArgumentNotExists(int index)
    {
        if (ArgumentCount <= index)
        {
            LuaRuntimeException.BadArgument(State, index + 1);
        }
    }
}
