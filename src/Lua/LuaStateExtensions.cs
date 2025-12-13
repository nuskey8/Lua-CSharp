using System.Runtime.CompilerServices;
using Lua.IO;
using Lua.Runtime;

// ReSharper disable MethodHasAsyncOverloadWithCancellation

namespace Lua;

public static class LuaStateExtensions
{
    public static async ValueTask<LuaClosure> LoadFileAsync(this LuaState state, string fileName, string mode, LuaTable? environment, CancellationToken cancellationToken)
    {
        var name = "@" + fileName;
        using var stream = await state.GlobalState.Platform.FileSystem.Open(fileName, LuaFileOpenMode.Read, cancellationToken);
        var source = await stream.ReadAllAsync(cancellationToken);
        var closure = state.Load(source, name, environment);

        return closure;
    }

    public static ValueTask<int> DoStringAsync(this LuaState state, string source, Memory<LuaValue> results, string? chunkName = null, CancellationToken cancellationToken = default)
    {
        var closure = state.Load(source, chunkName ?? source);
        return ExecuteAsync(state, closure, results, cancellationToken);
    }

    public static ValueTask<LuaValue[]> DoStringAsync(this LuaState state, string source, string? chunkName = null, CancellationToken cancellationToken = default)
    {
        var closure = state.Load(source, chunkName ?? source);
        return ExecuteAsync(state, closure, cancellationToken);
    }

    public static ValueTask<int> ExecuteAsync(this LuaState state, ReadOnlySpan<byte> source, Memory<LuaValue> results, string chunkName, CancellationToken cancellationToken = default)
    {
        var closure = state.Load(source, chunkName);
        return ExecuteAsync(state, closure, results, cancellationToken);
    }

    public static ValueTask<LuaValue[]> ExecuteAsync(this LuaState state, ReadOnlySpan<byte> source, string chunkName, CancellationToken cancellationToken = default)
    {
        var closure = state.Load(source, chunkName);
        return ExecuteAsync(state, closure, cancellationToken);
    }

    public static async ValueTask<int> DoFileAsync(this LuaState state, string path, Memory<LuaValue> buffer, CancellationToken cancellationToken = default)
    {
        var closure = await state.LoadFileAsync(path, "bt", null, cancellationToken);
        var count = await state.RunAsync(closure, 0, cancellationToken);
        using var results = state.ReadStack(count);
        results.AsSpan()[..Math.Min(buffer.Length, results.Length)].CopyTo(buffer.Span);
        return results.Count;
    }

    public static async ValueTask<LuaValue[]> DoFileAsync(this LuaState state, string path, CancellationToken cancellationToken = default)
    {
        var closure = await state.LoadFileAsync(path, "bt", null, cancellationToken);
        var count = await state.RunAsync(closure, 0, cancellationToken);
        using var results = state.ReadStack(count);
        return results.AsSpan().ToArray();
    }

    public static async ValueTask<int> ExecuteAsync(this LuaState state, LuaClosure closure, Memory<LuaValue> buffer, CancellationToken cancellationToken = default)
    {
        var count = await state.RunAsync(closure, 0, cancellationToken);
        using var results = state.ReadStack(count);
        results.AsSpan()[..Math.Min(buffer.Length, results.Length)].CopyTo(buffer.Span);
        return results.Count;
    }

    public static async ValueTask<LuaValue[]> ExecuteAsync(this LuaState state, LuaClosure closure, CancellationToken cancellationToken = default)
    {
        var count = await state.RunAsync(closure, 0, cancellationToken);
        using var results = state.ReadStack(count);
        return results.AsSpan().ToArray();
    }

    public static void Push(this LuaState state, LuaValue value)
    {
        state.Stack.Push(value);
    }

    public static void Push(this LuaState state, params ReadOnlySpan<LuaValue> span)
    {
        state.Stack.PushRange(span);
    }

    public static void Pop(this LuaState state, int count)
    {
        state.Stack.Pop(count);
    }

    public static LuaValue Pop(this LuaState state)
    {
        return state.Stack.Pop();
    }

    public static LuaStackReader ReadStack(this LuaState state, int argumentCount)
    {
        var stack = state.Stack;
        return new(stack, stack.Count - argumentCount);
    }

    public static ValueTask<LuaValue> AddAsync(this LuaState state, LuaValue x, LuaValue y, CancellationToken cancellationToken = default)
    {
        if (x.TryReadDouble(out var numX) && y.TryReadDouble(out var numY))
        {
            return new(numX + numY);
        }

        return LuaVirtualMachine.ExecuteBinaryOperationMetaMethod(state, x, y, OpCode.Add, cancellationToken);
    }

    public static ValueTask<LuaValue> SubAsync(this LuaState state, LuaValue x, LuaValue y, CancellationToken cancellationToken = default)
    {
        if (x.TryReadDouble(out var numX) && y.TryReadDouble(out var numY))
        {
            return new(numX - numY);
        }

        return LuaVirtualMachine.ExecuteBinaryOperationMetaMethod(state, x, y, OpCode.Sub, cancellationToken);
    }

    public static ValueTask<LuaValue> MulAsync(this LuaState state, LuaValue x, LuaValue y, CancellationToken cancellationToken = default)
    {
        if (x.TryReadDouble(out var numX) && y.TryReadDouble(out var numY))
        {
            return new(numX * numY);
        }

        return LuaVirtualMachine.ExecuteBinaryOperationMetaMethod(state, x, y, OpCode.Mul, cancellationToken);
    }

    public static ValueTask<LuaValue> DivAsync(this LuaState state, LuaValue x, LuaValue y, CancellationToken cancellationToken = default)
    {
        if (x.TryReadDouble(out var numX) && y.TryReadDouble(out var numY))
        {
            return new(numX / numY);
        }

        return LuaVirtualMachine.ExecuteBinaryOperationMetaMethod(state, x, y, OpCode.Div, cancellationToken);
    }

    public static ValueTask<LuaValue> ModAsync(this LuaState state, LuaValue x, LuaValue y, CancellationToken cancellationToken = default)
    {
        if (x.TryReadDouble(out var numX) && y.TryReadDouble(out var numY))
        {
            return new(LuaVirtualMachine.Mod(numX, numY));
        }

        return LuaVirtualMachine.ExecuteBinaryOperationMetaMethod(state, x, y, OpCode.Mod, cancellationToken);
    }

    public static ValueTask<LuaValue> PowAsync(this LuaState state, LuaValue x, LuaValue y, CancellationToken cancellationToken = default)
    {
        if (x.TryReadDouble(out var numX) && y.TryReadDouble(out var numY))
        {
            return new(Math.Pow(numX, numY));
        }

        return LuaVirtualMachine.ExecuteBinaryOperationMetaMethod(state, x, y, OpCode.Pow, cancellationToken);
    }


    public static ValueTask<LuaValue> UnmAsync(this LuaState state, LuaValue value, CancellationToken cancellationToken = default)
    {
        if (value.TryReadDouble(out var numB))
        {
            return new(-numB);
        }

        return LuaVirtualMachine.ExecuteUnaryOperationMetaMethod(state, value, OpCode.Unm, cancellationToken);
    }

    public static ValueTask<LuaValue> LenAsync(this LuaState state, LuaValue value, CancellationToken cancellationToken = default)
    {
        if (value.TryReadString(out var str))
        {
            return new(str.Length);
        }

        if (value.TryReadTable(out var table))
        {
            return new(table.ArrayLength);
        }

        return LuaVirtualMachine.ExecuteUnaryOperationMetaMethod(state, value, OpCode.Len, cancellationToken);
    }


    public static ValueTask<bool> LessThanAsync(this LuaState state, LuaValue x, LuaValue y, CancellationToken cancellationToken = default)
    {
        if (x.TryReadNumber(out var numX) && y.TryReadNumber(out var numY))
        {
            return new(numX < numY);
        }

        if (x.TryReadString(out var strX) && y.TryReadString(out var strY))
        {
            var c = StringComparer.Ordinal.Compare(strX, strY);
            return new(c < 0);
        }

        return LuaVirtualMachine.ExecuteCompareOperationMetaMethod(state, x, y, OpCode.Lt, cancellationToken);
    }

    public static ValueTask<bool> LessThanOrEqualsAsync(this LuaState state, LuaValue x, LuaValue y, CancellationToken cancellationToken = default)
    {
        if (x.TryReadNumber(out var numX) && y.TryReadNumber(out var numY))
        {
            return new(numX <= numY);
        }

        if (x.TryReadString(out var strX) && y.TryReadString(out var strY))
        {
            var c = StringComparer.Ordinal.Compare(strX, strY);
            return new(c <= 0);
        }

        return LuaVirtualMachine.ExecuteCompareOperationMetaMethod(state, x, y, OpCode.Le, cancellationToken);
    }

    public static ValueTask<bool> EqualsAsync(this LuaState state, LuaValue x, LuaValue y, CancellationToken cancellationToken = default)
    {
        if (x == y)
        {
            return new(true);
        }

        return LuaVirtualMachine.ExecuteCompareOperationMetaMethod(state, x, y, OpCode.Eq, cancellationToken);
    }

    public static async ValueTask<LuaValue> GetTableAsync(this LuaState state, LuaValue table, LuaValue key, CancellationToken cancellationToken = default)
    {
        if (table.TryReadTable(out var luaTable))
        {
            if (luaTable.TryGetValue(key, out var value))
            {
                return value;
            }
        }

        return await LuaVirtualMachine.ExecuteGetTableSlowPath(state, table, key, cancellationToken);
    }

    public static ValueTask SetTableAsync(this LuaState state, LuaValue table, LuaValue key, LuaValue value, CancellationToken cancellationToken = default)
    {
        if (key.TryReadNumber(out var numB))
        {
            if (double.IsNaN(numB))
            {
                throw new LuaRuntimeException(state, "table index is NaN");
            }
        }


        if (table.TryReadTable(out var luaTable))
        {
            ref var valueRef = ref luaTable.FindValue(key);
            if (!Unsafe.IsNullRef(ref valueRef) && valueRef.Type != LuaValueType.Nil)
            {
                valueRef = value;
                return default;
            }
        }

        return LuaVirtualMachine.ExecuteSetTableSlowPath(state, table, key, value, cancellationToken);
    }

    public static ValueTask<LuaValue> ConcatAsync(this LuaState state, ReadOnlySpan<LuaValue> values, CancellationToken cancellationToken = default)
    {
        state.Stack.PushRange(values);
        return ConcatAsync(state, values.Length, cancellationToken);
    }

    public static ValueTask<LuaValue> ConcatAsync(this LuaState state, int concatCount, CancellationToken cancellationToken = default)
    {
        return LuaVirtualMachine.Concat(state, concatCount, cancellationToken);
    }

    public static ValueTask<int> CallAsync(this LuaState state, int funcIndex, CancellationToken cancellationToken = default)
    {
        return LuaVirtualMachine.Call(state, funcIndex, funcIndex, cancellationToken);
    }
    
    public static ValueTask<int> CallAsync(this LuaState state, int funcIndex, int returnBase, CancellationToken cancellationToken = default)
    {
        return LuaVirtualMachine.Call(state, funcIndex, returnBase, cancellationToken);
    }

    public static ValueTask<LuaValue[]> CallAsync(this LuaState state, LuaValue function, ReadOnlySpan<LuaValue> arguments, CancellationToken cancellationToken = default)
    {
        var funcIndex = state.Stack.Count;
        state.Stack.Push(function);
        state.Stack.PushRange(arguments);
        return Impl(state, funcIndex, cancellationToken);

        static async ValueTask<LuaValue[]> Impl(LuaState state, int funcIndex, CancellationToken cancellationToken)
        {
            await LuaVirtualMachine.Call(state, funcIndex, funcIndex, cancellationToken);
            var count = state.Stack.Count - funcIndex;
            using var results = state.ReadStack(count);
            return results.AsSpan().ToArray();
        }
    }
}