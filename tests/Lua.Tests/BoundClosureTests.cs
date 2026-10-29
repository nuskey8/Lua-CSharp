using Lua.Runtime;
using Lua.Standard;

namespace Lua.Tests;

public class BoundClosureTests
{
    [Test]
    public async Task SharedCallbackUsesItsOwnTarget()
    {
        using var state = LuaState.Create();
        state.OpenStandardLibraries();
        var first = new List<int>();
        var second = new List<int>();
        state.Environment["first"] = new LuaTable
        {
            ["add"] = new BoundClosure<List<int>>("add", first, Add),
        };
        state.Environment["second"] = new LuaTable
        {
            ["add"] = new BoundClosure<List<int>>("add", second, Add),
        };

        await state.DoStringAsync(
            """
            local saved = first.add
            assert(first.add(10) == 1)
            assert(second.add(20) == 1)
            local function tail() return saved(30) end
            assert(tail() == 2)
            assert(second.add(40) == 2)
            """
        );

        Assert.That(first, Is.EqualTo(new[] { 10, 30 }));
        Assert.That(second, Is.EqualTo(new[] { 20, 40 }));
        Assert.That(state.CallStackFrameCount, Is.Zero);
        Assert.That(state.Stack.Count, Is.Zero);

        static ValueTask<int> Add(LuaFunctionExecutionContext context, CancellationToken ct)
        {
            var closure = (BoundClosure<List<int>>)context.State.GetCurrentFrame().Function;
            Assert.That(context.ArgumentCount, Is.EqualTo(1));
            closure.Target.Add(context.GetArgument<int>(0));
            return new(context.Return(closure.Target.Count));
        }
    }

    [Test]
    public async Task ValueTypeTargetDoesNotChangeArguments()
    {
        using var state = LuaState.Create();
        state.Environment["bound"] = new BoundClosure<int>(
            "bound",
            42,
            static (context, ct) =>
            {
                var closure = (BoundClosure<int>)context.State.GetCurrentFrame().Function;
                return new(context.Return([closure.Target, .. context.Arguments]));
            }
        );

        var results = await state.DoStringAsync("return bound('hello', nil, 123)");

        Assert.That(results, Is.EqualTo(new LuaValue[] { 42, "hello", LuaValue.Nil, 123 }));
    }

    [Test]
    public async Task TargetIsSeparateFromLuaUpvalues()
    {
        using var state = LuaState.Create();
        state.OpenStandardLibraries();
        state.Environment["bound"] = new BoundClosure<string>(
            "bound",
            "original",
            static (context, ct) =>
            {
                var closure = (BoundClosure<string>)context.State.GetCurrentFrame().Function;
                return new(context.Return(closure.Target));
            }
        );

        await state.DoStringAsync(
            """
            assert(debug.getupvalue(bound, 1) == nil)
            assert(debug.setupvalue(bound, 1, 'replacement') == nil)
            assert(bound() == 'original')
            """
        );
    }
}
