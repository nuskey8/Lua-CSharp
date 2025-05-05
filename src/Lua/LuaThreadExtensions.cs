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
        thread.CoreData!.Stack.Push(value);
    }

    public static void Push(this LuaThread thread, params ReadOnlySpan<LuaValue> span)
    {
        thread.CoreData!.Stack.PushRange(span);
    }

    public static void Pop(this LuaThread thread, int count)
    {
        thread.CoreData!.Stack.Pop(count);
    }

    public static LuaValue Pop(this LuaThread thread)
    {
        return thread.CoreData!.Stack.Pop();
    }

    public static LuaReturnValuesReader ReadReturnValues(this LuaThread thread, int argumentCount)
    {
        var stack = thread.CoreData!.Stack;
        return new LuaReturnValuesReader(stack, stack.Count - argumentCount);
    }

    public static ref readonly CallStackFrame GetCurrentFrame(this LuaThread thread)
    {
        return ref thread.CoreData!.CallStack.PeekRef();
    }

    public static ReadOnlySpan<LuaValue> GetStackValues(this LuaThread thread)
    {
        if (thread.CoreData == null) return default;
        return thread.CoreData!.Stack.AsSpan();
    }

    public static ReadOnlySpan<CallStackFrame> GetCallStackFrames(this LuaThread thread)
    {
        if (thread.CoreData == null) return default;
        return thread.CoreData!.CallStack.AsSpan();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static void PushCallStackFrame(this LuaThread thread, in CallStackFrame frame)
    {
        thread.CoreData!.CallStack.Push(frame);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static void PopCallStackFrameWithStackPop(this LuaThread thread)
    {
        var coreData = thread.CoreData!;

        coreData.Stack.PopUntil(coreData!.CallStack.Pop().Base);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static void PopCallStackFrameWithStackPop(this LuaThread thread, int frameBase)
    {
        var coreData = thread.CoreData!;
        coreData!.CallStack.Pop();
        {
            coreData.Stack.PopUntil(frameBase);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static void PopCallStackFrame(this LuaThread thread)
    {
        var coreData = thread.CoreData!;
        coreData!.CallStack.Pop();
    }

    public static async ValueTask<LuaValue> OpArithmetic(this LuaThread thread, LuaValue left, LuaValue right, OpCode opCode, CancellationToken ct = default)
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


        if (left.TryReadDouble(out var numB) && right.TryReadDouble(out var numC))
        {
            return ArithmeticOperation(opCode, numB, numC);
        }

        return await LuaVirtualMachine.ExecuteBinaryOperationMetaMethod(thread, left, right, opCode, ct);
    }

    public static async ValueTask<LuaValue> OpUnary(this LuaThread thread, LuaValue left, OpCode opCode, CancellationToken ct = default)
    {
        if (opCode == OpCode.Unm)
        {
            if (left.TryReadDouble(out var numB))
            {
                return -numB;
            }
        }
        else if (opCode == OpCode.Len)
        {
            if (left.TryReadString(out var str))
            {
                return str.Length;
            }

            if (left.TryReadTable(out var table))
            {
                return table.ArrayLength;
            }
        }
        else
        {
            throw new InvalidOperationException($"Unsupported unary operation: {opCode}");
        }


        return await LuaVirtualMachine.ExecuteUnaryOperationMetaMethod(thread, left, opCode, ct);
    }


    public static async ValueTask<bool> OpCompare(this LuaThread thread, LuaValue vb, LuaValue vc, OpCode opCode, CancellationToken ct = default)
    {
        if (opCode is not (OpCode.Eq or OpCode.Lt or OpCode.Le))
        {
            throw new InvalidOperationException($"Unsupported compare operation: {opCode}");
        }

        if (opCode == OpCode.Eq)
        {
            if (vb == vc)
            {
                return true;
            }
        }
        else
        {
            if (vb.TryReadNumber(out var numB) && vc.TryReadNumber(out var numC))
            {
                return opCode == OpCode.Lt ? numB < numC : numB <= numC;
            }

            if (vb.TryReadString(out var strB) && vc.TryReadString(out var strC))
            {
                var c = StringComparer.Ordinal.Compare(strB, strC);
                return opCode == OpCode.Lt ? c < 0 : c <= 0;
            }
        }


        return await LuaVirtualMachine.ExecuteCompareOperationMetaMethod(thread, vb, vc, opCode, ct);
    }

    public static ValueTask<LuaValue> OpGetTable(this LuaThread thread, LuaValue table, LuaValue key, CancellationToken ct = default)
    {
        if (table.TryReadTable(out var luaTable))
        {
            if (luaTable.TryGetValue(key, out var value))
            {
                return new(value);
            }
        }

        return LuaVirtualMachine.ExecuteGetTableSlowPath(thread, table, key, ct);
    }

    public static ValueTask OpSetTable(this LuaThread thread, LuaValue table, LuaValue key, LuaValue value, CancellationToken ct = default)
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
                return default;
            }
        }

        return LuaVirtualMachine.ExecuteSetTableSlowPath(thread, table, key, value, ct);
    }
    
    public static ValueTask<LuaValue> OpConcat(this LuaThread thread, ReadOnlySpan<LuaValue> values,CancellationToken ct = default)
    {
        thread.Stack.PushRange(values);
        return OpConcat(thread, values.Length, ct);
    }
    public static ValueTask<LuaValue> OpConcat(this LuaThread thread, int concatCount,CancellationToken ct = default)
    {
        
        return LuaVirtualMachine.Concat(thread,  concatCount, ct);
    }
}