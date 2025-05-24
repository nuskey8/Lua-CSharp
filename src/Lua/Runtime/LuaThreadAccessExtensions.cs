using System.Runtime.CompilerServices;

// ReSharper disable MethodHasAsyncOverloadWithCancellation

namespace Lua.Runtime;

public static class LuaThreadAccessAccessExtensions
{
    public static async ValueTask<int> DoStringAsync(this LuaThreadAccess access, string source, Memory<LuaValue> buffer, string? chunkName = null, CancellationToken cancellationToken = default)
    {
        access.ThrowIfInvalid();
        var closure = access.State.Load(source, chunkName ?? source);
        var count = await access.RunAsync(closure, 0, cancellationToken);
        using var results = access.ReadReturnValues(count);
        results.AsSpan()[..Math.Min(buffer.Length, count)].CopyTo(buffer.Span);
        return count;
    }

    public static async ValueTask<LuaValue[]> DoStringAsync(this LuaThreadAccess access, string source, string? chunkName = null, CancellationToken cancellationToken = default)
    {
        access.ThrowIfInvalid();
        var closure = access.State.Load(source, chunkName ?? source);
        var count = await access.RunAsync(closure, 0, cancellationToken);
        using var results = access.ReadReturnValues(count);
        return results.AsSpan().ToArray();
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


    public static async ValueTask<LuaValue> Arithmetic(this LuaThreadAccess access, LuaValue x, LuaValue y, OpCode opCode, CancellationToken cancellationToken = default)
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        static double Mod(double a, double b)
        {
            var mod = a % b;
            if ((b > 0 && mod < 0) || (b < 0 && mod > 0))
            {
                mod += b;
            }

            return mod;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static double ArithmeticOperation(OpCode code, double a, double b)
        {
            return code switch
            {
                OpCode.Add => a + b,
                OpCode.Sub => a - b,
                OpCode.Mul => a * b,
                OpCode.Div => a / b,
                OpCode.Mod => Mod(a, b),
                OpCode.Pow => Math.Pow(a, b),
                _ => throw new InvalidOperationException($"Unsupported arithmetic operation: {code}"),
            };
        }


        if (x.TryReadDouble(out var numX) && y.TryReadDouble(out var numY))
        {
            return ArithmeticOperation(opCode, numX, numY);
        }

        access.ThrowIfInvalid();
        return await LuaVirtualMachine.ExecuteBinaryOperationMetaMethod(access.Thread, x, y, opCode, cancellationToken);
    }

    public static async ValueTask<LuaValue> Unary(this LuaThreadAccess access, LuaValue value, OpCode opCode, CancellationToken cancellationToken = default)
    {
        if (opCode == OpCode.Unm)
        {
            if (value.TryReadDouble(out var numB))
            {
                return -numB;
            }
        }
        else if (opCode == OpCode.Len)
        {
            if (value.TryReadString(out var str))
            {
                return str.Length;
            }

            if (value.TryReadTable(out var table))
            {
                return table.ArrayLength;
            }
        }
        else
        {
            throw new InvalidOperationException($"Unsupported unary operation: {opCode}");
        }

        access.ThrowIfInvalid();
        return await LuaVirtualMachine.ExecuteUnaryOperationMetaMethod(access.Thread, value, opCode, cancellationToken);
    }


    public static async ValueTask<bool> Compare(this LuaThreadAccess access, LuaValue x, LuaValue y, OpCode opCode, CancellationToken cancellationToken = default)
    {
        if (opCode is not (OpCode.Eq or OpCode.Lt or OpCode.Le))
        {
            throw new InvalidOperationException($"Unsupported compare operation: {opCode}");
        }

        if (opCode == OpCode.Eq)
        {
            if (x == y)
            {
                return true;
            }
        }
        else
        {
            if (x.TryReadNumber(out var numX) && y.TryReadNumber(out var numY))
            {
                return opCode == OpCode.Lt ? numX < numY : numX <= numY;
            }

            if (x.TryReadString(out var strX) && y.TryReadString(out var strY))
            {
                var c = StringComparer.Ordinal.Compare(strX, strY);
                return opCode == OpCode.Lt ? c < 0 : c <= 0;
            }
        }

        access.ThrowIfInvalid();
        return await LuaVirtualMachine.ExecuteCompareOperationMetaMethod(access.Thread, x, y, opCode, cancellationToken);
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

    public static async ValueTask SetTable(this LuaThreadAccess access, LuaValue table, LuaValue key, LuaValue value, CancellationToken cancellationToken = default)
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
                return;
            }
        }

        await LuaVirtualMachine.ExecuteSetTableSlowPath(access.Thread, table, key, value, cancellationToken);
    }

    public static ValueTask<LuaValue> Concat(this LuaThreadAccess access, ReadOnlySpan<LuaValue> values, CancellationToken cancellationToken = default)
    {
        access.ThrowIfInvalid();
        access.Stack.PushRange(values);
        return Concat(access, values.Length, cancellationToken);
    }

    public static async ValueTask<LuaValue> Concat(this LuaThreadAccess access, int concatCount, CancellationToken cancellationToken = default)
    {
        access.ThrowIfInvalid();
        return await LuaVirtualMachine.Concat(access.Thread, concatCount, cancellationToken);
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