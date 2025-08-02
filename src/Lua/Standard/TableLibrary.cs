using Lua.CodeAnalysis;
using Lua.Internal;
using System.Runtime.CompilerServices;
using System.Text;
using Lua.Runtime;
using System.Globalization;

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

    // TODO: optimize
    static readonly Prototype defaultComparer = new(
        "comp", 0, 0, 2, 2, false,
        [],
        [
            Instruction.Le(1, 0, 1),
            Instruction.LoadBool(2, 1, 1),
            Instruction.LoadBool(2, 0, 0),
            Instruction.Return(2, 2)
        ], [], [0, 0, 0, 0], [new() { Name = "a", StartPc = 0, EndPc = 4 }, new() { Name = "b", StartPc = 0, EndPc = 4 }],
        []
    );

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

    public async ValueTask<int> Sort(LuaFunctionExecutionContext context, CancellationToken cancellationToken)
    {
        var arg0 = context.GetArgument<LuaTable>(0);
        var arg1 = context.HasArgument(1)
            ? context.GetArgument<LuaFunction>(1)
            : new LuaClosure(context.State, defaultComparer);

        // discard extra  arguments
        context = context with { ArgumentCount = 2 };
        context.State.Stack.PopUntil(context.FrameBase + 2);

        context.State.PushCallStackFrame(new() { Base = context.FrameBase, ReturnBase = context.ReturnFrameBase, VariableArgumentCount = 0, Function = arg1 });
        try
        {
            await QuickSortAsync(context, arg0.GetArrayMemory(), 0, arg0.ArrayLength - 1, arg1, cancellationToken);
            return context.Return();
        }
        finally
        {
            context.State.PopCallStackFrameWithStackPop();
        }
    }

    async ValueTask QuickSortAsync(LuaFunctionExecutionContext context, Memory<LuaValue> memory, int low, int high, LuaFunction comparer, CancellationToken cancellationToken)
    {
        if (low < high)
        {
            var pivotIndex = await PartitionAsync(context, memory, low, high, comparer, cancellationToken);
            await QuickSortAsync(context, memory, low, pivotIndex - 1, comparer, cancellationToken);
            await QuickSortAsync(context, memory, pivotIndex + 1, high, comparer, cancellationToken);
        }
    }

    async ValueTask<int> PartitionAsync(LuaFunctionExecutionContext context, Memory<LuaValue> memory, int low, int high, LuaFunction comparer, CancellationToken cancellationToken)
    {
        var pivot = memory.Span[high];
        var i = low - 1;
        var state = context.State;

        for (var j = low; j < high; j++)
        {
            var stack = state.Stack;
            var top = stack.Count;
            stack.Push(memory.Span[j]);
            stack.Push(pivot);
            await state.RunAsync(comparer, 2, cancellationToken);

            if (state.Stack.Get(top).ToBoolean())
            {
                i++;
                Swap(memory.Span, i, j);
            }

            state.Stack.PopUntil(top);
        }

        Swap(memory.Span, i + 1, high);
        return i + 1;
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