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

    [Test]
    public async Task CancellationWaitsForYieldedResultTransfer(
        [Values] bool protectedMode,
        [Values] bool cancelResume,
        [Values] bool oversized
    )
    {
        using var state = CreateState();
        using var cancellation = new CancellationTokenSource();
        var count = oversized ? OversizedResultCount : 1;
        using var thread = state.CreateCoroutine(
            new LuaFunction(
                async (context, token) =>
                {
                    context.GetReturnBuffer(count).Fill(42);
                    var pending = context.State.YieldAsync(
                        context with
                        {
                            ArgumentCount = count,
                        },
                        cancelResume ? token : cancellation.Token
                    );
                    // The first resume has not returned from RunAsync to copy the published yield.
                    cancellation.Cancel();
                    Assert.That(pending.IsCompleted, Is.False);
                    Assert.That(context.State.Stack.Count, Is.EqualTo(count));
                    Assert.That(context.State.Stack[count - 1], Is.EqualTo(new LuaValue(42)));
                    return await pending;
                }
            ),
            protectedMode
        );
        var results = new LuaStack();
        var resumeToken = cancelResume ? cancellation.Token : default;
        if (oversized)
        {
            await AssertTransferError(thread, results, protectedMode, resumeToken);
        }
        else
        {
            Assert.That(await thread.ResumeAsync(results, resumeToken), Is.EqualTo(2));
            Assert.That(results.AsSpan().ToArray(), Is.EqualTo(new LuaValue[] { true, 42 }));
        }
        Assert.That(() => thread.GetStatus(), Is.EqualTo(LuaThreadStatus.Dead).After(1000, 10));
        Assert.That(thread.CoroutineFunction, Is.Null);
        Assert.That(thread.GetStackValues().Length, Is.Zero);
        Assert.That(thread.CallStackFrameCount, Is.Zero);
    }

    [Test]
    public async Task OversizedInitialArgumentsPreserveTheEntryForRetry(
        [Values] bool protectedMode,
        [Values] bool asynchronous
    )
    {
        using var state = CreateState();
        using var cancellation = new CancellationTokenSource();
        var entered = false;
        var entry = new LuaFunction(
            async (context, _) =>
            {
                entered = true;
                if (asynchronous)
                    await Task.Yield();
                return context.Return(context.GetArgument(0));
            }
        );
        using var thread = state.CreateCoroutine(entry, protectedMode);
        var arguments = new LuaStack(OversizedResultCount + 1);
        arguments.NotifyTop(OversizedResultCount + 1);
        if (protectedMode)
        {
            Assert.That(await thread.ResumeAsync(arguments, cancellation.Token), Is.EqualTo(2));
            Assert.That(
                arguments.AsSpan().ToArray(),
                Is.EqualTo(new LuaValue[] { false, "too many arguments to resume" })
            );
        }
        else
        {
            var error = Assert.ThrowsAsync<LuaRuntimeException>(async () =>
                await thread.ResumeAsync(arguments, cancellation.Token)
            )!;
            Assert.That(error.Message, Does.Contain("too many arguments to resume"));
            Assert.That(arguments.Count, Is.EqualTo(OversizedResultCount + 1));
        }
        Assert.That(entered, Is.False);
        Assert.That(thread.GetStatus(), Is.EqualTo(LuaThreadStatus.Suspended));
        Assert.That(thread.CanResume, Is.True);
        Assert.That(thread.CoroutineFunction, Is.SameAs(entry));
        Assert.That(thread.GetStackValues().Length, Is.Zero);
        Assert.That(thread.CallStackFrameCount, Is.Zero);
        cancellation.Cancel();

        var retry = new LuaStack();
        retry.Push(42);
        Assert.That(await thread.ResumeAsync(retry), Is.EqualTo(2));
        Assert.That(retry.AsSpan().ToArray(), Is.EqualTo(new LuaValue[] { true, 42 }));
        Assert.That(entered, Is.True);
        Assert.That(thread.GetStatus(), Is.EqualTo(LuaThreadStatus.Dead));
        Assert.That(thread.CoroutineFunction, Is.Null);
        Assert.That(thread.GetStackValues().Length, Is.Zero);
        Assert.That(thread.CallStackFrameCount, Is.Zero);
    }

    [Test]
    public async Task OversizedResumeArgumentsPreserveSuspension(
        [Values] bool protectedMode,
        [Values] bool asynchronous,
        [Values] bool externalStack
    )
    {
        using var state = CreateState();
        using var cancellation = new CancellationTokenSource();
        var yielded = new LuaStack();
        yielded.Push(7);
        state.Environment["pause"] = new LuaFunction(
            async (context, _) =>
            {
                if (asynchronous)
                    await Task.Yield();
                return context.Return();
            }
        );
        state.Environment["wait"] = new LuaFunction(
            async (context, token) =>
            {
                if (!externalStack)
                    return await context.State.YieldAsync(context, token);
                await context.State.YieldAsync(yielded, token);
                return context.Return(yielded.AsSpan());
            }
        );
        using var thread = state.CreateCoroutine(
            state.Load(
                """
                local value,x=42,17
                saved=function() return value,x end
                pause()
                x=wait(7)
                return value,x
                """,
                "resume-arguments"
            ),
            protectedMode
        );
        var results = new LuaStack();
        await thread.ResumeAsync(results);
        Assert.That(results.AsSpan().ToArray(), Is.EqualTo(new LuaValue[] { true, 7 }));
        var frameCount = thread.CallStackFrameCount;
        var argumentCount = OversizedResultCount + (externalStack ? 1 : 0);
        var arguments = new LuaStack(argumentCount);
        arguments.NotifyTop(argumentCount);
        if (protectedMode)
        {
            Assert.That(await thread.ResumeAsync(arguments, cancellation.Token), Is.EqualTo(2));
            Assert.That(
                arguments.AsSpan().ToArray(),
                Is.EqualTo(new LuaValue[] { false, "too many arguments to resume" })
            );
        }
        else
        {
            var error = Assert.ThrowsAsync<LuaRuntimeException>(async () =>
                await thread.ResumeAsync(arguments, cancellation.Token)
            )!;
            Assert.That(error.Message, Does.Contain("too many arguments to resume"));
            Assert.That(arguments.Count, Is.EqualTo(argumentCount));
        }
        Assert.That(thread.GetStatus(), Is.EqualTo(LuaThreadStatus.Suspended));
        Assert.That(thread.CanResume, Is.True);
        Assert.That(thread.CallStackFrameCount, Is.EqualTo(frameCount));
        Assert.That(thread.CoroutineFunction, Is.Not.Null);
        Assert.That(
            await state.DoStringAsync("return saved()"),
            Is.EqualTo(new LuaValue[] { 42, 17 })
        );
        cancellation.Cancel();

        results.Clear();
        results.Push(9);
        Assert.That(await thread.ResumeAsync(results), Is.EqualTo(3));
        Assert.That(results.AsSpan().ToArray(), Is.EqualTo(new LuaValue[] { true, 42, 9 }));
        Assert.That(
            await state.DoStringAsync("return saved()"),
            Is.EqualTo(new LuaValue[] { 42, 9 })
        );
        Assert.That(thread.GetStatus(), Is.EqualTo(LuaThreadStatus.Dead));
        Assert.That(thread.CoroutineFunction, Is.Null);
        Assert.That(thread.CallStackFrameCount, Is.Zero);
        Assert.That(thread.GetStackValues().Length, Is.Zero);
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
