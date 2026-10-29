using System.Runtime.CompilerServices;
using Lua.Platforms;
using Lua.Runtime;
using Lua.Standard;

namespace Lua.Tests;

[TestFixture(LuaStringMode.Utf16)]
[TestFixture(LuaStringMode.Bytes)]
public class CoroutineTransferTests(LuaStringMode mode)
{
    const int OversizedResultCount = 1 << 19;

    LuaState CreateState()
    {
        var state = LuaState.Create(
            mode == LuaStringMode.Bytes ? LuaPlatform.DefaultByteStrings : LuaPlatform.Default
        );
        state.OpenStandardLibraries();
        return state;
    }

    [Test]
    public async Task TransferredValuesAreCollectibleWhileLocalsRemainAlive(
        [Values] bool wrap,
        [Values] bool asynchronous
    )
    {
        using var state = CreateState();
        var references = new List<WeakReference>();
        state.Environment["wrap"] = wrap;
        state.Environment["make"] = new LuaFunction(
            async (context, _) =>
            {
                if (asynchronous)
                    await Task.Yield();
                return MakePayload(context, references);
            }
        );
        await state.DoStringAsync(
            """
            local function body()
                co=coroutine.running()
                local keep=make()
                local plain=17
                get=function() return keep end
                local x,y=coroutine.yield(make(),nil,73)
                return keep,plain,x,y
            end
            local value,empty,n
            if wrap then
                run=coroutine.wrap(body)
                value,empty,n=run()
            else
                co=coroutine.create(body)
                local ok
                ok,value,empty,n=coroutine.resume(co)
                assert(ok)
            end
            assert(type(value)=='userdata' and empty==nil and n==73)
            """
        );
        Collect();
        Assert.That(references.Count, Is.EqualTo(2));
        Assert.That(references[0].IsAlive, Is.True);
        Assert.That(references[1].IsAlive, Is.False);
        var thread = state.Environment["co"].Read<LuaState>();
        Assert.That(thread.GetStatus(), Is.EqualTo(LuaThreadStatus.Suspended));

        var result = await state.DoStringAsync(
            wrap ? "return run(42,'resumed')" : "return coroutine.resume(co,42,'resumed')"
        );
        LuaValue[] expected = [LuaValue.FromObject(references[0].Target!), 17, 42, "resumed"];
        Assert.That(result, Is.EqualTo(wrap ? expected : new LuaValue[] { true }.Concat(expected)));
        Assert.That(thread.GetStatus(), Is.EqualTo(LuaThreadStatus.Dead));
        Assert.That(thread.CoroutineFunction, Is.Null);
        Assert.That(thread.GetStackValues().Length, Is.Zero);
    }

    [Test]
    public async Task OversizedYieldReleasesResultsAndPreservesSuspendedExecution(
        [Values] bool protectedMode,
        [Values] bool asynchronous,
        [Values] bool cancel
    )
    {
        using var state = CreateState();
        using var cancellation = new CancellationTokenSource();
        var yielded = new LuaStack(OversizedResultCount);
        yielded.NotifyTop(OversizedResultCount);
        var reference = StorePayload(yielded.AsSpan());
        var thread = state.CreateCoroutine(
            new LuaFunction(
                async (context, token) =>
                {
                    if (asynchronous)
                        await Task.Yield();
                    var count = await context.State.YieldAsync(yielded, token);
                    return context.Return(yielded[0], count);
                }
            ),
            protectedMode
        );
        try
        {
            var results = new LuaStack();
            await AssertTransferError(thread, results, protectedMode, cancellation.Token);
            Assert.That(thread.GetStatus(), Is.EqualTo(LuaThreadStatus.Suspended));
            Assert.That(thread.CanResume, Is.True);
            Assert.That(thread.CallStackFrameCount, Is.GreaterThan(0));
            Assert.That(thread.CoroutineFunction, Is.Not.Null);
            Assert.That(yielded.Count, Is.Zero);
            Collect();
            Assert.That(reference.IsAlive, Is.False);

            if (cancel)
            {
                cancellation.Cancel();
                await Task.Yield();
                Assert.That(
                    () => thread.GetStatus(),
                    Is.EqualTo(LuaThreadStatus.Dead).After(1000, 10)
                );
            }
            else
            {
                results.Clear();
                results.Push(42);
                Assert.That(await thread.ResumeAsync(results), Is.EqualTo(3));
                Assert.That(results.AsSpan().ToArray(), Is.EqualTo(new LuaValue[] { true, 42, 1 }));
                Assert.That(thread.GetStatus(), Is.EqualTo(LuaThreadStatus.Dead));
            }
            Assert.That(thread.CoroutineFunction, Is.Null);
            Assert.That(thread.GetStackValues().Length, Is.Zero);
            Assert.That(thread.CallStackFrameCount, Is.Zero);
        }
        finally
        {
            cancellation.Cancel();
            if (thread.CallStackFrameCount == 0)
                thread.Dispose();
        }
    }

    [Test]
    public async Task LuaTransferErrorRestoresTheResumerAndAllowsRetry([Values] bool wrap)
    {
        using var state = CreateState();
        var yielded = new LuaStack(OversizedResultCount);
        yielded.NotifyTop(OversizedResultCount);
        var reference = StorePayload(yielded.AsSpan());
        state.Environment["wrap"] = wrap;
        state.Environment["many"] = new LuaFunction(
            async (context, token) =>
            {
                await context.State.YieldAsync(yielded, token);
                return context.Return();
            }
        );
        var result = await state.DoStringAsync(
            """
            local function body()
                co=coroutine.running()
                many()
                return 42
            end
            if wrap then run=coroutine.wrap(body) else co=coroutine.create(body) end
            local parent=coroutine.create(function()
                local ok,err
                if wrap then ok,err=pcall(run) else ok,err=coroutine.resume(co) end
                assert(coroutine.status(coroutine.running())=='running')
                return ok,err
            end)
            return coroutine.resume(parent)
            """
        );
        Assert.That(result.Length, Is.EqualTo(3));
        Assert.That(result[0], Is.EqualTo(new LuaValue(true)));
        Assert.That(result[1], Is.EqualTo(new LuaValue(false)));
        Assert.That(result[2].Read<string>(), Does.Contain("too many results to resume"));
        var thread = state.Environment["co"].Read<LuaState>();
        Assert.That(thread.GetStatus(), Is.EqualTo(LuaThreadStatus.Suspended));
        Assert.That(yielded.Count, Is.Zero);
        Collect();
        Assert.That(reference.IsAlive, Is.False);
        result = await state.DoStringAsync(wrap ? "return run()" : "return coroutine.resume(co)");
        Assert.That(result, Is.EqualTo(wrap ? new LuaValue[] { 42 } : new LuaValue[] { true, 42 }));
        Assert.That(thread.GetStatus(), Is.EqualTo(LuaThreadStatus.Dead));
        Assert.That(thread.CoroutineFunction, Is.Null);
        Assert.That(state.Stack.Count, Is.Zero);
        Assert.That(state.CallStackFrameCount, Is.Zero);
    }

    [Test]
    public async Task OversizedFinalReturnReleasesCompletedExecution(
        [Values] bool protectedMode,
        [Values] bool asynchronous
    )
    {
        using var state = CreateState();
        WeakReference? reference = null;
        using var thread = state.CreateCoroutine(
            new LuaFunction(
                async (context, _) =>
                {
                    if (asynchronous)
                        await Task.Yield();
                    reference = StorePayload(context.GetReturnBuffer(OversizedResultCount));
                    return OversizedResultCount;
                }
            ),
            protectedMode
        );
        await AssertTransferError(thread, new LuaStack(), protectedMode);
        Assert.That(thread.GetStatus(), Is.EqualTo(LuaThreadStatus.Dead));
        Assert.That(thread.CoroutineFunction, Is.Null);
        Assert.That(thread.GetStackValues().Length, Is.Zero);
        Assert.That(thread.CallStackFrameCount, Is.Zero);
        Collect();
        Assert.That(reference, Is.Not.Null);
        Assert.That(reference!.IsAlive, Is.False);
    }

    static async Task AssertTransferError(
        LuaState thread,
        LuaStack results,
        bool protectedMode,
        CancellationToken cancellationToken = default
    )
    {
        if (protectedMode)
        {
            Assert.That(await thread.ResumeAsync(results, cancellationToken), Is.EqualTo(2));
            Assert.That(
                results.AsSpan().ToArray(),
                Is.EqualTo(new LuaValue[] { false, "too many results to resume" })
            );
        }
        else
        {
            var error = Assert.ThrowsAsync<LuaRuntimeException>(async () =>
                await thread.ResumeAsync(results, cancellationToken)
            )!;
            Assert.That(error.Message, Does.Contain("too many results to resume"));
            Assert.That(results.Count, Is.Zero);
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static int MakePayload(LuaFunctionExecutionContext context, List<WeakReference> references)
    {
        var payload = new object();
        references.Add(new(payload));
        return context.Return(LuaValue.FromObject(payload));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static WeakReference StorePayload(Span<LuaValue> values)
    {
        var payload = new object();
        values[0] = LuaValue.FromObject(payload);
        return new(payload);
    }

    static void Collect()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }
}
