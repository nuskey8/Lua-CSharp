using System.Runtime.CompilerServices;
using Lua.Runtime;
using Lua.Standard;

namespace Lua.Tests;

public class CoroutineLifetimeTests
{
    [TestCase(false, false)]
    [TestCase(true, false)]
    [TestCase(false, true)]
    [TestCase(true, true)]
    public void CancellationBeforeYieldPropagatesAndReleasesExecution(
        bool protectedMode,
        bool asynchronous
    )
    {
        using var state = LuaState.Create();
        using var source = new CancellationTokenSource();
        using var thread = state.CreateCoroutine(
            new LuaFunction(
                async (context, token) =>
                {
                    if (asynchronous)
                        await Task.Yield();
                    source.Cancel();
                    return await context.State.YieldAsync(context, token);
                }
            ),
            protectedMode
        );
        Assert.CatchAsync<OperationCanceledException>(async () =>
            await thread.ResumeAsync(new LuaStack(), source.Token)
        );
        Assert.That(thread.GetStatus(), Is.EqualTo(LuaThreadStatus.Dead));
        Assert.That(thread.CallStackFrameCount, Is.Zero);
        Assert.That(thread.GetStackValues().Length, Is.Zero);
    }

    [TestCase(false, false)]
    [TestCase(true, false)]
    [TestCase(false, true)]
    [TestCase(true, true)]
    public async Task LaterResumeTokenCancelsTheNextYield(bool protectedMode, bool alreadyCancelled)
    {
        using var state = LuaState.Create();
        using var source = new CancellationTokenSource();
        using var thread = state.CreateCoroutine(
            new LuaFunction(
                async (context, token) =>
                {
                    context.State.Stack.Push(1);
                    await context.State.YieldAsync(context with { ArgumentCount = 1 }, token);
                    context.State.Stack.Push(2);
                    return await context.State.YieldAsync(
                        context with
                        {
                            ArgumentCount = 1,
                        },
                        token
                    );
                }
            ),
            protectedMode
        );
        var stack = new LuaStack();
        await thread.ResumeAsync(stack);
        Assert.That(stack.AsSpan().ToArray(), Is.EqualTo(new LuaValue[] { true, 1 }));
        stack.Clear();
        if (alreadyCancelled)
        {
            source.Cancel();
            Assert.CatchAsync<OperationCanceledException>(async () =>
                await thread.ResumeAsync(stack, source.Token)
            );
        }
        else
        {
            await thread.ResumeAsync(stack, source.Token);
            Assert.That(stack.AsSpan().ToArray(), Is.EqualTo(new LuaValue[] { true, 2 }));
            source.Cancel();
        }
        Assert.That(() => thread.GetStatus(), Is.EqualTo(LuaThreadStatus.Dead).After(1000, 10));
        Assert.That(thread.CallStackFrameCount, Is.Zero);
        Assert.That(thread.GetStackValues().Length, Is.Zero);
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task EitherDistinctCancellationTokenCanCancelSuspendedYield(bool cancelResume)
    {
        using var state = LuaState.Create();
        using var resumeSource = new CancellationTokenSource();
        using var yieldSource = new CancellationTokenSource();
        using var thread = state.CreateCoroutine(
            new LuaFunction((context, _) => context.State.YieldAsync(context, yieldSource.Token))
        );
        var stack = new LuaStack();
        stack.Push(42);
        await thread.ResumeAsync(stack, resumeSource.Token);
        Assert.That(stack.AsSpan().ToArray(), Is.EqualTo(new LuaValue[] { true, 42 }));
        (cancelResume ? resumeSource : yieldSource).Cancel();
        Assert.That(() => thread.GetStatus(), Is.EqualTo(LuaThreadStatus.Dead).After(1000, 10));
        Assert.That(thread.CallStackFrameCount, Is.Zero);
        Assert.That(thread.GetStackValues().Length, Is.Zero);
    }

    [TestCase(false, false)]
    [TestCase(true, false)]
    [TestCase(false, true)]
    [TestCase(true, true)]
    public void CancellingSuspendedCoroutineReleasesExecution(bool protectedMode, bool asynchronous)
    {
        using var state = LuaState.Create();
        state.OpenStandardLibraries();
        using var cancellation = new CancellationTokenSource();
        var (thread, reference) = StartSuspendedCoroutine(
            state,
            cancellation.Token,
            protectedMode,
            asynchronous
        );
        using (thread)
        {
            cancellation.Cancel();
            Assert.That(() => thread.GetStatus(), Is.EqualTo(LuaThreadStatus.Dead).After(1000, 10));
            Assert.That(thread.CanResume, Is.False);
            Assert.That(thread.GetStackValues().Length, Is.Zero);
            Assert.That(thread.CallStackFrameCount, Is.Zero);
            Assert.That(thread.LuaTraceback!.ToString(), Does.Contain("suspended-cancellation"));
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            Assert.That(reference.IsAlive, Is.False);
            var results = new LuaStack();
            if (protectedMode)
            {
                thread.ResumeAsync(results).AsTask().GetAwaiter().GetResult();
                Assert.That(results[0], Is.EqualTo(new LuaValue(false)));
                Assert.That(
                    results[1].Read<string>(),
                    Does.Contain("cannot resume dead coroutine")
                );
            }
            else
            {
                Assert.ThrowsAsync<LuaRuntimeException>(async () =>
                    await thread.ResumeAsync(results)
                );
            }
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static (LuaState, WeakReference) StartSuspendedCoroutine(
        LuaState state,
        CancellationToken token,
        bool protectedMode,
        bool asynchronous
    )
    {
        var payload = new object();
        state.Environment["payload"] = LuaValue.FromObject(payload);
        state.Environment["pause"] = new LuaFunction(
            async (context, _) =>
            {
                if (asynchronous)
                    await Task.Yield();
                return context.Return();
            }
        );
        var thread = state.CreateCoroutine(
            state.Load(
                "pause(); local p=payload; payload=nil; coroutine.yield(42); return p",
                "suspended-cancellation"
            ),
            protectedMode
        );
        var stack = new LuaStack();
        thread.ResumeAsync(stack, token).AsTask().GetAwaiter().GetResult();
        Assert.That(stack.AsSpan().ToArray(), Is.EqualTo(new LuaValue[] { true, 42 }));
        return (thread, new WeakReference(payload));
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task ManagedCoroutineBodyCanYieldAndReceiveResumeArguments(bool asynchronous)
    {
        using var state = LuaState.Create();
        using var thread = state.CreateCoroutine(
            new LuaFunction(
                async (context, token) =>
                {
                    if (asynchronous)
                        await Task.Yield();
                    return await context.State.YieldAsync(context, token);
                }
            )
        );
        var stack = new LuaStack();
        stack.Push(7);
        await thread.ResumeAsync(stack);
        Assert.That(stack.AsSpan().ToArray(), Is.EqualTo(new LuaValue[] { true, 7 }));
        stack.Clear();
        stack.Push(42);
        await thread.ResumeAsync(stack);
        Assert.That(stack.AsSpan().ToArray(), Is.EqualTo(new LuaValue[] { true, 42 }));
        Assert.That(thread.GetStatus(), Is.EqualTo(LuaThreadStatus.Dead));
    }

    [Test]
    public async Task YieldAcrossNestedManagedCallStillReportsLuaError()
    {
        using var state = LuaState.Create();
        var nested = new LuaFunction((context, token) => context.State.YieldAsync(context, token));
        using var thread = state.CreateCoroutine(
            new LuaFunction((context, token) => context.State.RunAsync(nested, token)),
            true
        );
        var stack = new LuaStack();
        await thread.ResumeAsync(stack);
        Assert.That(stack[0], Is.EqualTo(new LuaValue(false)));
        Assert.That(
            stack[1].Read<string>(),
            Does.Contain("attempt to yield across a C#-call boundary")
        );
    }

    [Test]
    public async Task CancelledCoroutineReleasesStackAndPreservesCancellation(
        [Values] bool wrap,
        [Values] bool yieldFirst,
        [Values] bool cancellableCallback
    )
    {
        using var state = LuaState.Create();
        state.OpenStandardLibraries();
        using var cancellation = new CancellationTokenSource();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        state.Environment["pause"] = new LuaFunction(
            async (context, token) =>
            {
                entered.SetResult();
                if (cancellableCallback)
                    await release.Task.WaitAsync(token);
                else
                    await release.Task;
                return context.Return();
            }
        );
        state.Environment["wrap"] = wrap;
        state.Environment["yieldFirst"] = yieldFirst;
        var execution = state
            .DoStringAsync(
                """
                local function body()
                    thread=coroutine.running()
                    local captured=42
                    saved=function() return captured end
                    if yieldFirst then coroutine.yield() end
                    pause()
                    return captured
                end
                if wrap then
                    run=coroutine.wrap(body)
                else
                    thread=coroutine.create(body)
                    run=function() return coroutine.resume(thread) end
                end
                if yieldFirst then run() end
                return run()
                """,
                "coroutine-cancellation",
                cancellation.Token
            )
            .AsTask();
        await entered.Task;
        cancellation.Cancel();
        release.TrySetResult();
        var exception = Assert.ThrowsAsync<LuaCanceledException>(async () => await execution)!;
        Assert.That(exception.CancellationToken, Is.EqualTo(cancellation.Token));
        Assert.That(exception.LuaTraceback!.ToString(), Does.Contain("coroutine-cancellation"));
        var thread = state.Environment["thread"].Read<LuaState>();
        Assert.That(thread.GetStatus(), Is.EqualTo(LuaThreadStatus.Dead));
        Assert.That(thread.GetStackValues().Length, Is.Zero);
        Assert.That(thread.CallStackFrameCount, Is.Zero);
        Assert.That(state.CallStackFrameCount, Is.Zero);
        Assert.That(state.GetStatus(), Is.EqualTo(LuaThreadStatus.Running));
        state.Stack.Clear();
        var results = await state.DoStringAsync(
            "local ok,err; if wrap then ok,err=pcall(run) else ok,err=run() end; return ok,err,saved()"
        );
        Assert.That(results[0], Is.EqualTo(new LuaValue(false)));
        Assert.That(results[1].Read<string>(), Does.Contain("cannot resume dead coroutine"));
        Assert.That(results[2], Is.EqualTo(new LuaValue(42)));
    }

    [TestCase(false, false)]
    [TestCase(true, false)]
    [TestCase(false, true)]
    [TestCase(true, true)]
    public async Task FailedCoroutineReleasesStackAndPreservesError(bool wrap, bool yieldFirst)
    {
        using var state = LuaState.Create();
        state.OpenStandardLibraries();
        state.Environment["wrap"] = wrap;
        state.Environment["yieldFirst"] = yieldFirst;
        var results = await state.DoStringAsync(
            """
            local thread
            local failure={}
            local read
            local function body()
                thread=coroutine.running()
                local captured=42
                read=function() return captured end
                if yieldFirst then coroutine.yield() end
                error(failure)
            end
            local run
            if wrap then
                run=coroutine.wrap(body)
            else
                thread=coroutine.create(body)
                run=function() return coroutine.resume(thread) end
            end
            local function resume()
                if wrap then return pcall(run) end
                return run()
            end
            if yieldFirst then resume() end
            local ok,err=resume()
            local again,why=resume()
            return thread,ok,err==failure,coroutine.status(thread),again,why,read(),debug.traceback(thread)
            """,
            "coroutine-lifetime"
        );
        var thread = results[0].Read<LuaState>();
        Assert.That(results[1], Is.EqualTo(new LuaValue(false)));
        Assert.That(results[2], Is.EqualTo(new LuaValue(true)));
        Assert.That(results[3].Read<string>(), Is.EqualTo("dead"));
        Assert.That(results[4], Is.EqualTo(new LuaValue(false)));
        Assert.That(results[5].Read<string>(), Does.Contain("cannot resume dead coroutine"));
        Assert.That(results[6], Is.EqualTo(new LuaValue(42)));
        Assert.That(results[7].Read<string>(), Does.Contain("coroutine-lifetime"));
        Assert.That(thread.GetStatus(), Is.EqualTo(LuaThreadStatus.Dead));
        Assert.That(thread.GetStackValues().Length, Is.Zero);
        Assert.That(thread.CallStackFrameCount, Is.Zero);
        Assert.That((await state.DoStringAsync("return 42"))[0], Is.EqualTo(new LuaValue(42)));
    }
}
