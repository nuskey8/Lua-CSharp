using System.Runtime.CompilerServices;
using Lua.Platforms;
using Lua.Runtime;
using Lua.Standard;

namespace Lua.Tests;

[TestFixture(LuaStringMode.Utf16)]
[TestFixture(LuaStringMode.Bytes)]
public class ComparisonLifetimeTests(LuaStringMode mode)
{
    LuaState CreateState() =>
        LuaState.Create(
            mode == LuaStringMode.Bytes ? LuaPlatform.DefaultByteStrings : LuaPlatform.Default
        );

    [TestCase("lt", false)]
    [TestCase("lt", true)]
    [TestCase("le", false)]
    [TestCase("le", true)]
    [TestCase("eq", false)]
    [TestCase("eq", true)]
    [TestCase("reverse-le", false)]
    [TestCase("reverse-le", true)]
    public async Task NativeComparisonsUseConstantStackSpace(string operation, bool asynchronous)
    {
        using var state = CreateState();
        var positions = new HashSet<int>();
        var calls = 0;
        var method = new LuaFunction(
            async (context, _) =>
            {
                positions.Add(context.ReturnFrameBase);
                calls++;
                if (asynchronous)
                    await Task.Yield();
                return context.Return(operation != "reverse-le");
            }
        );
        var metatable = new LuaTable
        {
            [operation == "reverse-le" ? "__lt" : "__" + operation] = method,
        };
        state.Environment["left"] = new LuaTable { Metatable = metatable };
        state.Environment["right"] = new LuaTable { Metatable = metatable };
        var symbol = operation switch
        {
            "lt" => "<",
            "eq" => "==",
            _ => "<=",
        };
        var result = await state.DoStringAsync(
            "local a,b=left,right;local n=0;for i=1,1000 do if a"
                + symbol
                + "b then n=n+1 end end;return n"
        );
        Assert.That(result, Is.EqualTo(new LuaValue[] { 1000 }));
        Assert.That(calls, Is.EqualTo(1000));
        Assert.That(positions.Count, Is.EqualTo(1));
        Assert.That(state.Stack.Count, Is.Zero);
        Assert.That(state.CallStackFrameCount, Is.Zero);
    }

    [Test]
    public async Task DiscardedComparisonResultsAreCollectibleDuringExecution()
    {
        using var state = CreateState();
        var payloads = new List<WeakReference>();
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var paused = false;
        var method = new LuaFunction((context, _) => new(ReturnPayload(context, payloads)));
        var metatable = new LuaTable { ["__lt"] = method };
        state.Environment["left"] = new LuaTable { Metatable = metatable };
        state.Environment["right"] = new LuaTable { Metatable = metatable };
        state.SetHook(
            new LuaFunction(
                async (context, _) =>
                {
                    if (payloads.Count >= 100 && !paused)
                    {
                        paused = true;
                        await gate.Task;
                    }
                    return context.Return();
                }
            ),
            "",
            64
        );
        var execution = state
            .DoStringAsync(
                "local a,b=left,right;local n=0;for i=1,1000 do if a<b then n=n+1 end end;return n"
            )
            .AsTask();
        try
        {
            Assert.That(paused, Is.True);
            Assert.That(execution.IsCompleted, Is.False);
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            Assert.That(payloads.Any(payload => payload.IsAlive), Is.False);
        }
        finally
        {
            gate.SetResult();
            await execution;
        }
        Assert.That(execution.Result, Is.EqualTo(new LuaValue[] { 1000 }));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static int ReturnPayload(LuaFunctionExecutionContext context, List<WeakReference> payloads)
    {
        var payload = new object();
        payloads.Add(new(payload));
        return context.Return(LuaValue.FromObject(payload));
    }
}
