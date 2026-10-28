using System.Runtime.CompilerServices;
using Lua.Platforms;
using Lua.Runtime;
using Lua.Standard;

namespace Lua.Tests;

[TestFixture(LuaStringMode.Utf16)]
[TestFixture(LuaStringMode.Bytes)]
public class ArgumentAdjustmentTests(LuaStringMode mode)
{
    [TestCase(false, false)]
    [TestCase(true, false)]
    [TestCase(false, true)]
    [TestCase(true, true)]
    public async Task SurplusArgumentsOutsideLuaRegisterWindowCanBeCollected(
        bool tailCall,
        bool callHook
    )
    {
        using var state = CreateState(false);
        var worker = (
            await state.DoStringAsync(
                "return function() local sum=0;for i=1,1000 do sum=sum+i end;return sum end"
            )
        )[0]
            .Read<LuaClosure>();
        state.Environment["worker"] = worker;
        var reference = InstallPayload(state);
        var observed = false;
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        state.SetHook(
            new LuaFunction(
                async (context, _) =>
                {
                    var caller = context.State.GetCallStackFrames()[^2].Function;
                    if (caller == worker && !observed)
                    {
                        observed = true;
                        await gate.Task;
                    }
                    return context.Return();
                }
            ),
            callHook ? "c" : "",
            callHook ? 0 : 128
        );
        var call = "worker(1,2,3,4,5,6,7,8,9,take())";
        var source = """
            local function take()
                local value=payload
                payload=nil
                return value
            end
            """;
        source += tailCall ? "\nreturn " + call : "\nlocal result=" + call + ";return result";
        var execution = state.DoStringAsync(source).AsTask();
        try
        {
            Assert.That(observed, Is.True);
            Assert.That(execution.IsCompleted, Is.False);
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            Assert.That(reference.IsAlive, Is.False);
        }
        finally
        {
            gate.SetResult();
            await execution;
        }
        Assert.That(execution.Result, Is.EqualTo(new LuaValue[] { 500500 }));
        Assert.That(state.Stack.Count, Is.Zero);
        Assert.That(state.CallStackFrameCount, Is.Zero);
        GC.KeepAlive(state);
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task VariableResultsCanExtendPastTheCallersRegisterWindow(bool hooks)
    {
        using var state = CreateState(hooks);
        var result = await state.DoStringAsync(
            "local function many() return 1,2,3,4,5,6,7,8,9,10 end;return 0,many()"
        );
        Assert.That(
            result,
            Is.EqualTo(Enumerable.Range(0, 11).Select(value => new LuaValue(value)).ToArray())
        );
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static WeakReference InstallPayload(LuaState state)
    {
        var payload = new object();
        state.Environment["payload"] = LuaValue.FromObject(payload);
        return new WeakReference(payload);
    }

    LuaState CreateState(bool hooks)
    {
        var state = LuaState.Create(
            mode == LuaStringMode.Bytes ? LuaPlatform.DefaultByteStrings : LuaPlatform.Default
        );
        state.OpenStandardLibraries();
        if (hooks)
            state.SetHook(new LuaFunction((context, _) => new(context.Return())), "cr");
        return state;
    }

    [TestCase(false, false)]
    [TestCase(false, true)]
    [TestCase(true, false)]
    [TestCase(true, true)]
    public async Task TailCallsFillMissingParameters(bool varargs, bool hooks)
    {
        using var state = CreateState(hooks);
        var parameters = varargs ? "a,b,..." : "a,b";
        foreach (var result in new[] { "a", "b" })
        {
            var values = await state.DoStringAsync(
                "local function f("
                    + parameters
                    + ") return "
                    + result
                    + " end; local stale={}; return f()"
            );
            Assert.That(values, Is.EqualTo(new LuaValue[] { LuaValue.Nil }));
        }
        var partial = await state.DoStringAsync(
            "local function f(" + parameters + ") return a,b end; local stale={}; return f(7)"
        );
        Assert.That(partial, Is.EqualTo(new LuaValue[] { 7, LuaValue.Nil }));
        Assert.That(state.Stack.Count, Is.Zero);
        Assert.That(state.CallStackFrameCount, Is.Zero);
    }

    [TestCase(false, false)]
    [TestCase(false, true)]
    [TestCase(true, false)]
    [TestCase(true, true)]
    public async Task RunAsyncFillsMissingParametersAndPreservesPrefix(bool varargs, bool hooks)
    {
        using var state = CreateState(hooks);
        var parameters = varargs ? "a,b,..." : "a,b";
        var function = (
            await state.DoStringAsync("return function(" + parameters + ") return b end")
        )[0]
            .Read<LuaFunction>();
        state.Push(42);
        var count = await state.RunAsync(function);
        using (var results = state.ReadStack(count))
            Assert.That(results.AsSpan().ToArray(), Is.EqualTo(new LuaValue[] { LuaValue.Nil }));
        Assert.That(state.Pop(), Is.EqualTo(new LuaValue(42)));
        Assert.That(state.CallStackFrameCount, Is.Zero);
    }

    [Test]
    public async Task NativeCallbacksKeepTheirActualArgumentCount()
    {
        using var state = CreateState(false);
        state.Environment["native"] = new LuaFunction(
            (context, _) => new(context.Return(context.ArgumentCount))
        );
        var result = await state.DoStringAsync("return native(),native(1,nil,3)");
        Assert.That(result, Is.EqualTo(new LuaValue[] { 0, 3 }));
    }
}
