using System.Runtime.CompilerServices;
using System.Globalization;
using Lua.Runtime;
using Lua.Internal;

namespace Lua.Standard;

public sealed class TableLibrary
{
    public static readonly TableLibrary Instance = new();

    public TableLibrary()
    {
        var libraryName = "table";
        Functions =
        [
            new(libraryName, "concat", Concat),
            new(libraryName, "insert", Insert),
            new(libraryName, "pack", Pack),
            new(libraryName, "remove", Remove),
            new(libraryName, "sort", Sort),
            new(libraryName, "unpack", Unpack)
        ];
    }

    public readonly LibraryFunction[] Functions;

    public ValueTask<int> Concat(LuaFunctionExecutionContext context, CancellationToken cancellationToken)
    {
        var arg0 = context.GetArgument<LuaTable>(0);
        var arg1 = context.HasArgument(1)
            ? context.GetArgument<string>(1)
            : "";
        var arg2 = context.HasArgument(2)
            ? (long)context.GetArgument<double>(2)
            : 1;
        var arg3 = context.HasArgument(3)
            ? (long)context.GetArgument<double>(3)
            : arg0.ArrayLength;

        using PooledList<char> builder = new(512);

        for (var i = arg2; i <= arg3; i++)
        {
            var value = arg0[i];

            if (value.Type is LuaValueType.String)
            {
                builder.AddRange(value.Read<string>());
            }
            else if (value.Type is LuaValueType.Number)
            {
                builder.AddRange(value.Read<double>().ToString(CultureInfo.InvariantCulture));
            }
            else
            {
                throw new LuaRuntimeException(context.State, $"invalid value ({value.Type}) at index {i} in table for 'concat'");
            }

            if (i != arg3)
            {
                builder.AddRange(arg1);
            }
        }

        return new(context.Return(builder.AsSpan().ToString()));
    }

    public ValueTask<int> Insert(LuaFunctionExecutionContext context, CancellationToken cancellationToken)
    {
        var table = context.GetArgument<LuaTable>(0);

        var value = context.HasArgument(2)
            ? context.GetArgument(2)
            : context.GetArgument(1);

        var pos_arg = context.HasArgument(2)
            ? context.GetArgument<double>(1)
            : table.ArrayLength + 1;

        LuaRuntimeException.ThrowBadArgumentIfNumberIsNotInteger(context.State, 2, pos_arg);

        var pos = (int)pos_arg;

        if (pos <= 0 || pos > table.ArrayLength + 1)
        {
            throw new LuaRuntimeException(context.State, "bad argument #2 to 'insert' (position out of bounds)");
        }

        table.Insert(pos, value);
        return new(context.Return());
    }

    public ValueTask<int> Pack(LuaFunctionExecutionContext context, CancellationToken cancellationToken)
    {
        LuaTable table = new(context.ArgumentCount, 1);

        var span = context.Arguments;
        for (var i = 0; i < span.Length; i++)
        {
            table[i + 1] = span[i];
        }

        table["n"] = span.Length;

        return new(context.Return(table));
    }

    public ValueTask<int> Remove(LuaFunctionExecutionContext context, CancellationToken cancellationToken)
    {
        var table = context.GetArgument<LuaTable>(0);
        var n_arg = context.HasArgument(1)
            ? context.GetArgument<double>(1)
            : table.ArrayLength;

        LuaRuntimeException.ThrowBadArgumentIfNumberIsNotInteger(context.State, 2, n_arg);

        var n = (int)n_arg;

        if ((!context.HasArgument(1) && n == 0) || n == table.GetArraySpan().Length + 1)
        {
            return new(context.Return(LuaValue.Nil));
        }

        if (n <= 0 || n > table.GetArraySpan().Length)
        {
            throw new LuaRuntimeException(context.State, "bad argument #2 to 'remove' (position out of bounds)");
        }

        return new(context.Return(table.RemoveAt(n)));
    }

    public ValueTask<int> Sort(LuaFunctionExecutionContext context, CancellationToken cancellationToken)
    {
        var table = context.GetArgument<LuaTable>(0);
        var comparer = context.HasArgument(1)
            ? context.GetArgument<LuaFunction>(1)
            : null;

        var length = table.ArrayLength;
        if (length <= 1)
        {
            return new(context.Return());
        }

        var sortTask = AuxSortAsync(context.State, table.GetArrayMemory()[..length], 0, length - 1, comparer, cancellationToken);
        if (sortTask.IsCompletedSuccessfully)
        {
            sortTask.GetAwaiter().GetResult();
            return new(context.Return());
        }

        return AwaitSortAsync(context, sortTask);
    }

    static async ValueTask<int> AwaitSortAsync(LuaFunctionExecutionContext context, ValueTask sortTask)
    {
        await sortTask;
        return context.Return();
    }

    async ValueTask AuxSortAsync(LuaState state, Memory<LuaValue> memory, int lo, int up, LuaFunction? comparer, CancellationToken cancellationToken)
    {
        while (lo < up)
        {
            if (await CompareAsync(state, comparer, memory.Span[up], memory.Span[lo], cancellationToken))
            {
                Swap(memory.Span, lo, up);
            }

            if (up - lo == 1)
            {
                return;
            }

            var pivotIndex = (lo + up) / 2;
            if (await CompareAsync(state, comparer, memory.Span[pivotIndex], memory.Span[lo], cancellationToken))
            {
                Swap(memory.Span, pivotIndex, lo);
            }
            else if (await CompareAsync(state, comparer, memory.Span[up], memory.Span[pivotIndex], cancellationToken))
            {
                Swap(memory.Span, pivotIndex, up);
            }

            if (up - lo == 2)
            {
                return;
            }

            Swap(memory.Span, pivotIndex, up - 1);
            pivotIndex = await PartitionAsync(state, memory, lo, up, comparer, cancellationToken);

            if (pivotIndex - lo < up - pivotIndex)
            {
                var sortTask = AuxSortAsync(state, memory, lo, pivotIndex - 1, comparer, cancellationToken);
                if (!sortTask.IsCompletedSuccessfully)
                {
                    await sortTask;
                }

                lo = pivotIndex + 1;
            }
            else
            {
                var sortTask = AuxSortAsync(state, memory, pivotIndex + 1, up, comparer, cancellationToken);
                if (!sortTask.IsCompletedSuccessfully)
                {
                    await sortTask;
                }

                up = pivotIndex - 1;
            }
        }
    }

    async ValueTask<int> PartitionAsync(LuaState state, Memory<LuaValue> memory, int lo, int up, LuaFunction? comparer, CancellationToken cancellationToken)
    {
        var pivot = memory.Span[up - 1];
        var i = lo;
        var j = up - 1;

        while (true)
        {
            while (await CompareAsync(state, comparer, memory.Span[++i], pivot, cancellationToken))
            {
                if (i == up - 1)
                {
                    throw new LuaRuntimeException(state, "invalid order function for sorting");
                }
            }

            while (await CompareAsync(state, comparer, pivot, memory.Span[--j], cancellationToken))
            {
                if (j < i)
                {
                    throw new LuaRuntimeException(state, "invalid order function for sorting");
                }
            }

            if (j < i)
            {
                Swap(memory.Span, up - 1, i);
                return i;
            }

            Swap(memory.Span, i, j);
        }
    }

    ValueTask<bool> CompareAsync(LuaState state, LuaFunction? comparer, LuaValue left, LuaValue right, CancellationToken cancellationToken)
    {
        if (comparer == null)
        {
            return state.LessThanAsync(left, right, cancellationToken);
        }

        var stack = state.Stack;
        var top = stack.Count;
        stack.Push(left);
        stack.Push(right);

        var runTask = state.RunAsync(comparer, 2, cancellationToken);
        if (runTask.IsCompletedSuccessfully)
        {
            var returnCount = runTask.GetAwaiter().GetResult();
            var result = returnCount > 0 && state.Stack.Get(top).ToBoolean();
            stack.PopUntil(top);
            return new(result);
        }

        return AwaitCompareAsync(state, top, runTask);
    }

    static async ValueTask<bool> AwaitCompareAsync(LuaState state, int top, ValueTask<int> runTask)
    {
        try
        {
            var returnCount = await runTask;
            return returnCount > 0 && state.Stack.Get(top).ToBoolean();
        }
        finally
        {
            state.Stack.PopUntil(top);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    void Swap(Span<LuaValue> span, int i, int j)
    {
        (span[i], span[j]) = (span[j], span[i]);
    }

    public ValueTask<int> Unpack(LuaFunctionExecutionContext context, CancellationToken cancellationToken)
    {
        var arg0 = context.GetArgument<LuaTable>(0);
        var arg1 = context.HasArgument(1)
            ? (long)context.GetArgument<double>(1)
            : 1;
        var arg2 = context.HasArgument(2)
            ? (long)context.GetArgument<double>(2)
            : arg0.ArrayLength;

        var index = 0;
        arg1 = Math.Min(arg1, arg2 + 1);
        var count = (int)(arg2 - arg1 + 1);
        var buffer = context.GetReturnBuffer(count);
        for (var i = arg1; i <= arg2; i++)
        {
            buffer[index] = arg0[i];
            index++;
        }

        return new(index);
    }
}