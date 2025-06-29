using System.Runtime.CompilerServices;

// ReSharper disable MethodHasAsyncOverloadWithCancellation

namespace Lua.Runtime;

public static class LuaThreadAccessAccessExtensions
{
    public static ValueTask<int> DoStringAsync(this LuaThreadAccess access, string source, Memory<LuaValue> results, string? chunkName = null, CancellationToken cancellationToken = default)
    {
        access.ThrowIfInvalid();
        var closure = access.State.Load(source, chunkName ?? source);
        return ExecuteAsync(access, closure, results, cancellationToken);
    }

    public static ValueTask<LuaValue[]> DoStringAsync(this LuaThreadAccess access, string source, string? chunkName = null, CancellationToken cancellationToken = default)
    {
        access.ThrowIfInvalid();
        var closure = access.State.Load(source, chunkName ?? source);
        return ExecuteAsync(access, closure, cancellationToken);
    }

    public static ValueTask<int> ExecuteAsync(this LuaThreadAccess access, ReadOnlySpan<byte> source, Memory<LuaValue> results, string chunkName, CancellationToken cancellationToken = default)
    {
        access.ThrowIfInvalid();
        var closure = access.State.Load(source, chunkName);
        return ExecuteAsync(access, closure, results, cancellationToken);
    }

    public static ValueTask<LuaValue[]> ExecuteAsync(this LuaThreadAccess access, ReadOnlySpan<byte> source, string chunkName, CancellationToken cancellationToken = default)
    {
        access.ThrowIfInvalid();
        var closure = access.State.Load(source, chunkName);
        return ExecuteAsync(access, closure, cancellationToken);
    }

    public static async ValueTask<int> DoFileAsync(this LuaThreadAccess access, string path, Memory<LuaValue> buffer, CancellationToken cancellationToken = default)
    {
        access.ThrowIfInvalid();
        var closure = await access.State.LoadFileAsync(path, "bt", null, cancellationToken);
        var count = await access.RunAsync(closure, 0, cancellationToken);
        using var results = access.ReadReturnValues(count);
        results.AsSpan()[..Math.Min(buffer.Length, results.Length)].CopyTo(buffer.Span);
        return results.Count;
    }

    public static async ValueTask<LuaValue[]> DoFileAsync(this LuaThreadAccess access, string path, CancellationToken cancellationToken = default)
    {
        var closure = await access.State.LoadFileAsync(path, "bt", null, cancellationToken);
        var count = await access.RunAsync(closure, 0, cancellationToken);
        using var results = access.ReadReturnValues(count);
        return results.AsSpan().ToArray();
    }

    public static async ValueTask<int> ExecuteAsync(this LuaThreadAccess access, LuaClosure closure, Memory<LuaValue> buffer, CancellationToken cancellationToken = default)
    {
        access.ThrowIfInvalid();
        var count = await access.RunAsync(closure, 0, cancellationToken);
        using var results = access.ReadReturnValues(count);
        results.AsSpan()[..Math.Min(buffer.Length, results.Length)].CopyTo(buffer.Span);
        return results.Count;
    }

    public static async ValueTask<LuaValue[]> ExecuteAsync(this LuaThreadAccess access, LuaClosure closure, CancellationToken cancellationToken = default)
    {
        access.ThrowIfInvalid();
        var count = await access.RunAsync(closure, 0, cancellationToken);
        using var results = access.ReadReturnValues(count);
        return results.AsSpan().ToArray();
    }

    public static void Push(this LuaThreadAccess access, LuaValue value)
    {
        access.ThrowIfInvalid();
        access.Stack.Push(value);
    }

    public static void Push(this LuaThreadAccess access, params ReadOnlySpan<LuaValue> span)
    {
        access.ThrowIfInvalid();
        access.Stack.PushRange(span);
    }

    public static void Pop(this LuaThreadAccess access, int count)
    {
        access.ThrowIfInvalid();
        access.Stack.Pop(count);
    }

    public static LuaValue Pop(this LuaThreadAccess access)
    {
        access.ThrowIfInvalid();
        return access.Stack.Pop();
    }

    public static LuaReturnValuesReader ReadReturnValues(this LuaThreadAccess access, int argumentCount)
    {
        access.ThrowIfInvalid();
        var stack = access.Stack;
        return new LuaReturnValuesReader(stack, stack.Count - argumentCount);
    }

    public static ValueTask<LuaValue> Add(this LuaThreadAccess access, LuaValue x, LuaValue y, CancellationToken cancellationToken = default)
    {
        if (x.TryReadDouble(out var numX) && y.TryReadDouble(out var numY))
        {
            return new(numX + numY);
        }

        access.ThrowIfInvalid();
        return LuaVirtualMachine.ExecuteBinaryOperationMetaMethod(access.Thread, x, y, OpCode.Add, cancellationToken);
    }

    public static ValueTask<LuaValue> Sub(this LuaThreadAccess access, LuaValue x, LuaValue y, CancellationToken cancellationToken = default)
    {
        if (x.TryReadDouble(out var numX) && y.TryReadDouble(out var numY))
        {
            return new(numX - numY);
        }

        access.ThrowIfInvalid();
        return LuaVirtualMachine.ExecuteBinaryOperationMetaMethod(access.Thread, x, y, OpCode.Sub, cancellationToken);
    }

    public static ValueTask<LuaValue> Mul(this LuaThreadAccess access, LuaValue x, LuaValue y, CancellationToken cancellationToken = default)
    {
        if (x.TryReadDouble(out var numX) && y.TryReadDouble(out var numY))
        {
            return new(numX * numY);
        }

        access.ThrowIfInvalid();
        return LuaVirtualMachine.ExecuteBinaryOperationMetaMethod(access.Thread, x, y, OpCode.Mul, cancellationToken);
    }

    public static ValueTask<LuaValue> Div(this LuaThreadAccess access, LuaValue x, LuaValue y, CancellationToken cancellationToken = default)
    {
        if (x.TryReadDouble(out var numX) && y.TryReadDouble(out var numY))
        {
            return new(numX / numY);
        }

        access.ThrowIfInvalid();
        return LuaVirtualMachine.ExecuteBinaryOperationMetaMethod(access.Thread, x, y, OpCode.Div, cancellationToken);
    }

    public static ValueTask<LuaValue> Mod(this LuaThreadAccess access, LuaValue x, LuaValue y, CancellationToken cancellationToken = default)
    {
        if (x.TryReadDouble(out var numX) && y.TryReadDouble(out var numY))
        {
            return new(LuaVirtualMachine.Mod(numX, numY));
        }

        access.ThrowIfInvalid();
        return LuaVirtualMachine.ExecuteBinaryOperationMetaMethod(access.Thread, x, y, OpCode.Mod, cancellationToken);
    }

    public static ValueTask<LuaValue> Pow(this LuaThreadAccess access, LuaValue x, LuaValue y, CancellationToken cancellationToken = default)
    {
        if (x.TryReadDouble(out var numX) && y.TryReadDouble(out var numY))
        {
            return new(Math.Pow(numX, numY));
        }

        access.ThrowIfInvalid();
        return LuaVirtualMachine.ExecuteBinaryOperationMetaMethod(access.Thread, x, y, OpCode.Pow, cancellationToken);
    }


    public static ValueTask<LuaValue> Unm(this LuaThreadAccess access, LuaValue value, CancellationToken cancellationToken = default)
    {
        if (value.TryReadDouble(out var numB))
        {
            return new(-numB);
        }

        access.ThrowIfInvalid();
        return LuaVirtualMachine.ExecuteUnaryOperationMetaMethod(access.Thread, value, OpCode.Unm, cancellationToken);
    }

    public static ValueTask<LuaValue> Len(this LuaThreadAccess access, LuaValue value, CancellationToken cancellationToken = default)
    {
        if (value.TryReadString(out var str))
        {
            return new(str.Length);
        }

        if (value.TryReadTable(out var table))
        {
            return new(table.ArrayLength);
        }

        access.ThrowIfInvalid();
        return LuaVirtualMachine.ExecuteUnaryOperationMetaMethod(access.Thread, value, OpCode.Len, cancellationToken);
    }


    public static ValueTask<bool> LessThan(this LuaThreadAccess access, LuaValue x, LuaValue y, CancellationToken cancellationToken = default)
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

        access.ThrowIfInvalid();
        return LuaVirtualMachine.ExecuteCompareOperationMetaMethod(access.Thread, x, y, OpCode.Lt, cancellationToken);
    }

    public static ValueTask<bool> LessThanOrEquals(this LuaThreadAccess access, LuaValue x, LuaValue y, CancellationToken cancellationToken = default)
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

        access.ThrowIfInvalid();
        return LuaVirtualMachine.ExecuteCompareOperationMetaMethod(access.Thread, x, y, OpCode.Le, cancellationToken);
    }

    public static ValueTask<bool> Equals(this LuaThreadAccess access, LuaValue x, LuaValue y, CancellationToken cancellationToken = default)
    {
        if (x == y)
        {
            return new(true);
        }

        access.ThrowIfInvalid();
        return LuaVirtualMachine.ExecuteCompareOperationMetaMethod(access.Thread, x, y, OpCode.Eq, cancellationToken);
    }


    public static async ValueTask<LuaValue> GetTable(this LuaThreadAccess access, LuaValue table, LuaValue key, CancellationToken cancellationToken = default)
    {
        if (table.TryReadTable(out var luaTable))
        {
            if (luaTable.TryGetValue(key, out var value))
            {
                return value;
            }
        }

        access.ThrowIfInvalid();
        return await LuaVirtualMachine.ExecuteGetTableSlowPath(access.Thread, table, key, cancellationToken);
    }

    public static ValueTask SetTable(this LuaThreadAccess access, LuaValue table, LuaValue key, LuaValue value, CancellationToken cancellationToken = default)
    {
        access.ThrowIfInvalid();

        if (key.TryReadNumber(out var numB))
        {
            if (double.IsNaN(numB))
            {
                throw new LuaRuntimeException(access.Thread, "table index is NaN");
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

        return LuaVirtualMachine.ExecuteSetTableSlowPath(access.Thread, table, key, value, cancellationToken);
    }

    public static ValueTask<LuaValue> Concat(this LuaThreadAccess access, ReadOnlySpan<LuaValue> values, CancellationToken cancellationToken = default)
    {
        access.ThrowIfInvalid();
        access.Stack.PushRange(values);
        return Concat(access, values.Length, cancellationToken);
    }

    public static ValueTask<LuaValue> Concat(this LuaThreadAccess access, int concatCount, CancellationToken cancellationToken = default)
    {
        access.ThrowIfInvalid();
        return LuaVirtualMachine.Concat(access.Thread, concatCount, cancellationToken);
    }

    public static ValueTask<int> Call(this LuaThreadAccess access, int funcIndex, int returnBase, CancellationToken cancellationToken = default)
    {
        access.ThrowIfInvalid();
        return LuaVirtualMachine.Call(access.Thread, funcIndex, returnBase, cancellationToken);
    }

    public static ValueTask<LuaValue[]> Call(this LuaThreadAccess access, LuaValue function, ReadOnlySpan<LuaValue> arguments, CancellationToken cancellationToken = default)
    {
        access.ThrowIfInvalid();
        var thread = access.Thread;
        var funcIndex = thread.Stack.Count;
        thread.Stack.Push(function);
        thread.Stack.PushRange(arguments);
        return Impl(access, funcIndex, cancellationToken);

        static async ValueTask<LuaValue[]> Impl(LuaThreadAccess access, int funcIndex, CancellationToken cancellationToken)
        {
            await LuaVirtualMachine.Call(access.Thread, funcIndex, funcIndex, cancellationToken);
            var count = access.Stack.Count - funcIndex;
            using var results = access.ReadReturnValues(count);
            return results.AsSpan().ToArray();
        }
    }
}