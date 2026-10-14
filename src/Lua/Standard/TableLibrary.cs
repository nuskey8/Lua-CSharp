using System.Globalization;
using System.Runtime.CompilerServices;
using Lua.Internal;
using Lua.Internal.CompilerServices;
using Lua.Runtime;

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
            new(libraryName, "unpack", Unpack),
        ];
    }

    public readonly LibraryFunction[] Functions;

    [AsyncMethodBuilder(typeof(LightAsyncValueTaskMethodBuilder<>))]
    static async ValueTask<int> GetLengthAsync(
        LuaState state,
        LuaTable table,
        CancellationToken cancellationToken
    )
    {
        LuaValue value = table;
        if (!value.TryGetMetamethod(state.GlobalState, Metamethods.Len, out _))
        {
            return table.ArrayLength;
        }

        var result = await LuaVirtualMachine.ExecuteUnaryOperationMetaMethod(
            state,
            value,
            OpCode.Len,
            cancellationToken
        );
        if (!result.TryReadDouble(out var length))
        {
            throw new LuaRuntimeException(state, "object length is not a number");
        }

        if (double.IsNaN(length) || length < int.MinValue || length > int.MaxValue)
        {
            throw new LuaRuntimeException(state, "object length is out of range");
        }

        return (int)length;
    }

    [AsyncMethodBuilder(typeof(LightAsyncValueTaskMethodBuilder<>))]
    public async ValueTask<int> Concat(
        LuaFunctionExecutionContext context,
        CancellationToken cancellationToken
    )
    {
        var arg0 = context.GetArgument<LuaTable>(0);
        var arg1 = context.HasArgument(1) ? context.GetArgument<string>(1) : "";
        var arg2 = context.HasArgument(2) ? (long)context.GetArgument<double>(2) : 1;
        var arg3 = context.HasArgument(3)
            ? (long)context.GetArgument<double>(3)
            : await GetLengthAsync(context.State, arg0, cancellationToken);

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
                throw new LuaRuntimeException(
                    context.State,
                    $"invalid value ({value.Type}) at index {i} in table for 'concat'"
                );
            }

            if (i != arg3)
            {
                builder.AddRange(arg1);
            }
        }

        return context.Return(builder.AsSpan().ToString());
    }

    [AsyncMethodBuilder(typeof(LightAsyncValueTaskMethodBuilder<>))]
    public async ValueTask<int> Insert(
        LuaFunctionExecutionContext context,
        CancellationToken cancellationToken
    )
    {
        var table = context.GetArgument<LuaTable>(0);

        var length = await GetLengthAsync(context.State, table, cancellationToken);
        if (length == int.MaxValue)
        {
            throw new LuaRuntimeException(context.State, "table length is out of range");
        }

        if (context.ArgumentCount is not (2 or 3))
        {
            throw new LuaRuntimeException(context.State, "wrong number of arguments to 'insert'");
        }
        var value = context.ArgumentCount == 3 ? context.GetArgument(2) : context.GetArgument(1);
        var position = context.ArgumentCount == 3 ? context.GetArgument<double>(1) : length + 1;
        LuaRuntimeException.ThrowBadArgumentIfNumberIsNotInteger(context.State, 2, position);
        if (context.ArgumentCount == 3 && (position < 1 || position > length + 1))
        {
            throw new LuaRuntimeException(
                context.State,
                "bad argument #2 to 'insert' (position out of bounds)"
            );
        }

        // Lua 5.2 uses the logical length but raw element reads and writes.
        var index = (int)position;
        if (table.TryInsertWithinArray(index, length, value))
        {
            return context.Return();
        }
        for (var i = length; i >= index; i--)
        {
            table[i + 1] = table[i];
        }
        table[index] = value;
        return context.Return();
    }

    public ValueTask<int> Pack(
        LuaFunctionExecutionContext context,
        CancellationToken cancellationToken
    )
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

    [AsyncMethodBuilder(typeof(LightAsyncValueTaskMethodBuilder<>))]
    public async ValueTask<int> Remove(
        LuaFunctionExecutionContext context,
        CancellationToken cancellationToken
    )
    {
        var table = context.GetArgument<LuaTable>(0);
        var length = await GetLengthAsync(context.State, table, cancellationToken);
        var position = context.HasArgument(1) ? context.GetArgument<double>(1) : length;
        LuaRuntimeException.ThrowBadArgumentIfNumberIsNotInteger(context.State, 2, position);
        if (position != length && (position < 1 || position > (long)length + 1))
        {
            throw new LuaRuntimeException(
                context.State,
                "bad argument #2 to 'remove' (position out of bounds)"
            );
        }

        var index = (long)position;
        if (
            index >= 1
            && index <= length
            && table.TryRemoveWithinArray((int)index, length, out var removed)
        )
        {
            return context.Return(removed);
        }
        var value = table[index];
        for (; index < length; index++)
        {
            table[index] = table[index + 1];
        }
        table[index] = LuaValue.Nil;
        return context.Return(value);
    }

    [AsyncMethodBuilder(typeof(LightAsyncValueTaskMethodBuilder<>))]
    public async ValueTask<int> Sort(
        LuaFunctionExecutionContext context,
        CancellationToken cancellationToken
    )
    {
        var table = context.GetArgument<LuaTable>(0);
        var comparer = context.HasArgument(1) ? context.GetArgument<LuaFunction>(1) : null;

        var length = await GetLengthAsync(context.State, table, cancellationToken);
        // A __len result can extend beyond the backing array; use raw table keys.
        if (length > 1)
        {
            await AuxSortAsync(context.State, table, 0, length - 1, comparer, cancellationToken);
        }
        return context.Return();
    }

    [AsyncMethodBuilder(typeof(LightAsyncValueTaskMethodBuilder))]
    async ValueTask AuxSortAsync(
        LuaState state,
        LuaTable table,
        int lo,
        int up,
        LuaFunction? comparer,
        CancellationToken cancellationToken
    )
    {
        while (lo < up)
        {
            if (
                await CompareAsync(
                    state,
                    comparer,
                    table.GetIntegerValue(up + 1),
                    table.GetIntegerValue(lo + 1),
                    cancellationToken
                )
            )
            {
                Swap(table, lo, up);
            }

            if (up - lo == 1)
            {
                return;
            }

            var pivotIndex = (lo + up) / 2;
            if (
                await CompareAsync(
                    state,
                    comparer,
                    table.GetIntegerValue(pivotIndex + 1),
                    table.GetIntegerValue(lo + 1),
                    cancellationToken
                )
            )
            {
                Swap(table, pivotIndex, lo);
            }
            else if (
                await CompareAsync(
                    state,
                    comparer,
                    table.GetIntegerValue(up + 1),
                    table.GetIntegerValue(pivotIndex + 1),
                    cancellationToken
                )
            )
            {
                Swap(table, pivotIndex, up);
            }

            if (up - lo == 2)
            {
                return;
            }

            Swap(table, pivotIndex, up - 1);
            pivotIndex = await PartitionAsync(state, table, lo, up, comparer, cancellationToken);

            if (pivotIndex - lo < up - pivotIndex)
            {
                var sortTask = AuxSortAsync(
                    state,
                    table,
                    lo,
                    pivotIndex - 1,
                    comparer,
                    cancellationToken
                );
                if (!sortTask.IsCompletedSuccessfully)
                {
                    await sortTask;
                }

                lo = pivotIndex + 1;
            }
            else
            {
                var sortTask = AuxSortAsync(
                    state,
                    table,
                    pivotIndex + 1,
                    up,
                    comparer,
                    cancellationToken
                );
                if (!sortTask.IsCompletedSuccessfully)
                {
                    await sortTask;
                }

                up = pivotIndex - 1;
            }
        }
    }

    [AsyncMethodBuilder(typeof(LightAsyncValueTaskMethodBuilder<>))]
    async ValueTask<int> PartitionAsync(
        LuaState state,
        LuaTable table,
        int lo,
        int up,
        LuaFunction? comparer,
        CancellationToken cancellationToken
    )
    {
        var pivot = table.GetIntegerValue(up);
        var i = lo;
        var j = up - 1;

        while (true)
        {
            while (
                await CompareAsync(
                    state,
                    comparer,
                    table.GetIntegerValue((++i) + 1),
                    pivot,
                    cancellationToken
                )
            )
            {
                if (i == up - 1)
                {
                    throw new LuaRuntimeException(state, "invalid order function for sorting");
                }
            }

            while (
                await CompareAsync(
                    state,
                    comparer,
                    pivot,
                    table.GetIntegerValue((--j) + 1),
                    cancellationToken
                )
            )
            {
                if (j < i)
                {
                    throw new LuaRuntimeException(state, "invalid order function for sorting");
                }
            }

            if (j < i)
            {
                Swap(table, up - 1, i);
                return i;
            }

            Swap(table, i, j);
        }
    }

    ValueTask<bool> CompareAsync(
        LuaState state,
        LuaFunction? comparer,
        LuaValue left,
        LuaValue right,
        CancellationToken cancellationToken
    )
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

    [AsyncMethodBuilder(typeof(LightAsyncValueTaskMethodBuilder<>))]
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
    void Swap(LuaTable table, int i, int j)
    {
        table.SwapIntegerValues(i + 1, j + 1);
    }

    [AsyncMethodBuilder(typeof(LightAsyncValueTaskMethodBuilder<>))]
    public async ValueTask<int> Unpack(
        LuaFunctionExecutionContext context,
        CancellationToken cancellationToken
    )
    {
        var arg0 = context.GetArgument<LuaTable>(0);
        var arg1 = context.HasArgument(1) ? (long)context.GetArgument<double>(1) : 1;
        var arg2 = context.HasArgument(2)
            ? (long)context.GetArgument<double>(2)
            : await GetLengthAsync(context.State, arg0, cancellationToken);

        var index = 0;
        arg1 = Math.Min(arg1, arg2 + 1);
        var count = (int)(arg2 - arg1 + 1);
        var buffer = context.GetReturnBuffer(count);
        for (var i = arg1; i <= arg2; i++)
        {
            buffer[index] = arg0[i];
            index++;
        }

        return index;
    }
}
