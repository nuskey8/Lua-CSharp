using Lua.Runtime;
using System.Runtime.CompilerServices;

namespace Lua;

public static class LuaThreadExtensions
{
    public static UseThreadLease RentUseThread(this LuaThread thread)
    {
        return new(LuaUserThread.Create(thread));
    }

    public static CoroutineLease RentCoroutine(this LuaThread thread, LuaFunction function, bool isProtectedMode = false)
    {
        return new(LuaCoroutine.Create(thread, function, isProtectedMode));
    }

    public static async ValueTask<int> DoStringAsync(this LuaThread thread, string source, Memory<LuaValue> buffer, string? chunkName = null, CancellationToken cancellationToken = default)
    {
        var closure = thread.State.Load(source, chunkName ?? source);
        var count = await thread.RunAsync(closure, cancellationToken);
        using var results = thread.ReadReturnValues(count);
        results.AsSpan()[..Math.Min(buffer.Length, count)].CopyTo(buffer.Span);
        return count;
    }

    public static async ValueTask<LuaValue[]> DoStringAsync(this LuaThread thread, string source, string? chunkName = null, CancellationToken cancellationToken = default)
    {
        var closure = thread.State.Load(source, chunkName ?? source);
        var count = await thread.RunAsync(closure, cancellationToken);
        using var results = thread.ReadReturnValues(count);
        return results.AsSpan().ToArray();
    }

    public static async ValueTask<int> DoFileAsync(this LuaThread thread, string path, Memory<LuaValue> buffer, CancellationToken cancellationToken = default)
    {
        var bytes = await File.ReadAllBytesAsync(path, cancellationToken);
        var fileName = "@" + path;
        var closure = thread.State.Load(bytes, fileName);
        var count = await thread.RunAsync(closure, cancellationToken);
        using var results = thread.ReadReturnValues(count);
        results.AsSpan()[..Math.Min(buffer.Length, results.Length)].CopyTo(buffer.Span);
        return results.Count;
    }

    public static async ValueTask<LuaValue[]> DoFileAsync(this LuaThread thread, string path, CancellationToken cancellationToken = default)
    {
        var bytes = await File.ReadAllBytesAsync(path, cancellationToken);
        var fileName = "@" + path;
        var closure = thread.State.Load(bytes, fileName);
        var count = await thread.RunAsync(closure, cancellationToken);
        using var results = thread.ReadReturnValues(count);
        return results.AsSpan().ToArray();
    }

    public static void Push(this LuaThread thread, LuaValue value)
    {
        thread.Stack.Push(value);
    }

    public static void Push(this LuaThread thread, params ReadOnlySpan<LuaValue> span)
    {
        thread.Stack.PushRange(span);
    }

    public static void Pop(this LuaThread thread, int count)
    {
        thread.Stack.Pop(count);
    }

    public static LuaValue Pop(this LuaThread thread)
    {
        return thread.Stack.Pop();
    }

    public static LuaReturnValuesReader ReadReturnValues(this LuaThread thread, int argumentCount)
    {
        var stack = thread.Stack;
        return new LuaReturnValuesReader(stack, stack.Count - argumentCount);
    }


    public static async ValueTask<LuaValue> Arithmetic(this LuaThread thread, LuaValue x, LuaValue y, OpCode opCode, CancellationToken cancellationToken = default)
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


        return await LuaVirtualMachine.ExecuteBinaryOperationMetaMethod(thread, x, y, opCode, cancellationToken);
    }

    public static async ValueTask<LuaValue> Unary(this LuaThread thread, LuaValue value, OpCode opCode, CancellationToken cancellationToken = default)
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


        return await LuaVirtualMachine.ExecuteUnaryOperationMetaMethod(thread, value, opCode, cancellationToken);
    }


    public static async ValueTask<bool> Compare(this LuaThread thread, LuaValue x, LuaValue y, OpCode opCode, CancellationToken cancellationToken = default)
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


        return await LuaVirtualMachine.ExecuteCompareOperationMetaMethod(thread, x, y, opCode, cancellationToken);
    }

    public static async ValueTask<LuaValue> GetTable(this LuaThread thread, LuaValue table, LuaValue key, CancellationToken cancellationToken = default)
    {
        if (table.TryReadTable(out var luaTable))
        {
            if (luaTable.TryGetValue(key, out var value))
            {
                return new(value);
            }
        }


        return await LuaVirtualMachine.ExecuteGetTableSlowPath(thread, table, key, cancellationToken);
    }

    public static async ValueTask SetTable(this LuaThread thread, LuaValue table, LuaValue key, LuaValue value, CancellationToken cancellationToken = default)
    {
        if (key.TryReadNumber(out var numB))
        {
            if (double.IsNaN(numB))
            {
                throw new LuaRuntimeException(thread, "table index is NaN");
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

        await LuaVirtualMachine.ExecuteSetTableSlowPath(thread, table, key, value, cancellationToken);
    }

    public static ValueTask<LuaValue> Concat(this LuaThread thread, ReadOnlySpan<LuaValue> values, CancellationToken cancellationToken = default)
    {
        thread.Stack.PushRange(values);
        return Concat(thread, values.Length, cancellationToken);
    }

    public static async ValueTask<LuaValue> Concat(this LuaThread thread, int concatCount, CancellationToken cancellationToken = default)
    {
        return await LuaVirtualMachine.Concat(thread, concatCount, cancellationToken);
    }

    public static async ValueTask<int> Call(this LuaThread thread, int funcIndex, CancellationToken cancellationToken = default)
    {
        return await LuaVirtualMachine.Call(thread, funcIndex, cancellationToken);
    }

    public static ValueTask<LuaValue[]> Call(this LuaThread thread, LuaValue function, ReadOnlySpan<LuaValue> arguments, CancellationToken cancellationToken = default)
    {
        var funcIndex = thread.Stack.Count;
        thread.Stack.Push(function);
        thread.Stack.PushRange(arguments);
        return Impl(thread, funcIndex, cancellationToken);

        static async ValueTask<LuaValue[]> Impl(LuaThread thread, int funcIndex, CancellationToken cancellationToken)
        {
            await LuaVirtualMachine.Call(thread, funcIndex, cancellationToken);
            var count = thread.Stack.Count - funcIndex;
            using var results = thread.ReadReturnValues(count);
            return results.AsSpan().ToArray();
        }
    }
}