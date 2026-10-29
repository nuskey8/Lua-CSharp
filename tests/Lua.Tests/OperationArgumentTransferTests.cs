using Lua.Platforms;
using Lua.Runtime;

namespace Lua.Tests;

[TestFixture(LuaStringMode.Utf16)]
[TestFixture(LuaStringMode.Bytes)]
public class OperationArgumentTransferTests(LuaStringMode mode)
{
    [Test]
    public async Task FailedArgumentTransferPreservesStackAndAllowsRetry(
        [Values(
            "add",
            "sub",
            "mul",
            "div",
            "mod",
            "pow",
            "concat",
            "unm",
            "len",
            "lt",
            "le",
            "eq",
            "reverse-le"
        )]
            string operation,
        [Values] bool callableTable
    )
    {
        using var state = LuaState.Create(
            mode == LuaStringMode.Bytes ? LuaPlatform.DefaultByteStrings : LuaPlatform.Default
        );
        var calls = 0;
        LuaValue[] expectedArguments = [];
        var function = new LuaFunction(
            (context, _) =>
            {
                calls++;
                Assert.That(context.Arguments.ToArray(), Is.EqualTo(expectedArguments));
                return new(context.Return(operation == "reverse-le" ? new LuaValue(false) : 42));
            }
        );
        LuaValue method = callableTable
            ? new LuaTable { Metatable = new LuaTable { ["__call"] = function } }
            : function;
        var metatable = new LuaTable
        {
            [operation == "reverse-le" ? "__lt" : "__" + operation] = method,
        };
        LuaValue left = new LuaTable { Metatable = metatable };
        LuaValue right = new LuaTable { Metatable = metatable };
        LuaValue[] operands = operation switch
        {
            "unm" or "len" => [left, left],
            "reverse-le" => [right, left],
            _ => [left, right],
        };
        expectedArguments = callableTable ? [method, .. operands] : operands;
        var top = (1 << 19) - 1 - (callableTable ? 1 : 0) - (operation == "concat" ? 2 : 0);
        state.Stack.EnsureCapacity(1 << 19);
        state.Stack.NotifyTop(top);
        state.Stack[top - 1] = "sentinel";

        Assert.ThrowsAsync<LuaStackOverflowException>(async () =>
            await ExecuteOperation(state, operation, left, right)
        );
        Assert.That(calls, Is.Zero);
        Assert.That(state.Stack.Count, Is.EqualTo(top));
        Assert.That(state.Stack[top - 1], Is.EqualTo(new LuaValue("sentinel")));
        Assert.That(state.Stack.GetBuffer()[top..].ToArray(), Is.All.EqualTo(LuaValue.Nil));
        Assert.That(state.CallStackFrameCount, Is.Zero);

        state.Stack.Clear();
        state.Stack.Push("sentinel");
        var result = await ExecuteOperation(state, operation, left, right);
        Assert.That(
            result,
            Is.EqualTo(operation is "lt" or "le" or "eq" or "reverse-le" ? new LuaValue(true) : 42)
        );
        Assert.That(calls, Is.EqualTo(1));
        Assert.That(state.Stack.AsSpan().ToArray(), Is.EqualTo(new LuaValue[] { "sentinel" }));
        Assert.That(state.CallStackFrameCount, Is.Zero);
    }

    static async ValueTask<LuaValue> ExecuteOperation(
        LuaState state,
        string operation,
        LuaValue left,
        LuaValue right
    )
    {
        if (operation == "concat")
        {
            await state.ConcatAsync([left, right]);
            return state.Stack.Pop();
        }
        return operation switch
        {
            "add" => await state.AddAsync(left, right),
            "sub" => await state.SubAsync(left, right),
            "mul" => await state.MulAsync(left, right),
            "div" => await state.DivAsync(left, right),
            "mod" => await state.ModAsync(left, right),
            "pow" => await state.PowAsync(left, right),
            "unm" => await state.UnmAsync(left),
            "len" => await state.LenAsync(left),
            "lt" => await state.LessThanAsync(left, right),
            "le" or "reverse-le" => await state.LessThanOrEqualsAsync(left, right),
            "eq" => await state.EqualsAsync(left, right),
            _ => throw new ArgumentOutOfRangeException(nameof(operation)),
        };
    }
}
