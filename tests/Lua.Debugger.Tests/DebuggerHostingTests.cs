using System.Collections.Concurrent;
using System.Reflection;
using System.Threading.Channels;
using Lua.Debugging;
using Lua.Runtime;
using Lua.Standard;

namespace Lua.Debugger.Tests;

public class DebuggerHostingTests
{
    [TestCase(LuaDebugPauseMode.Asynchronous, LuaDebugStep.Over, "return")]
    [TestCase(LuaDebugPauseMode.Synchronous, LuaDebugStep.Over, "return")]
    [TestCase(LuaDebugPauseMode.Asynchronous, LuaDebugStep.Out, "return")]
    [TestCase(LuaDebugPauseMode.Synchronous, LuaDebugStep.Out, "return")]
    [TestCase(LuaDebugPauseMode.Asynchronous, LuaDebugStep.Out, "error")]
    [TestCase(LuaDebugPauseMode.Asynchronous, LuaDebugStep.Out, "cancel")]
    public async Task TopLevelStepsEndWithTheirInvocationWithoutClearingBreakpoints(
        LuaDebugPauseMode mode,
        LuaDebugStep step,
        string outcome
    )
    {
        using var state = LuaState.Create();
        using var debugger = new LuaDebugger(state, new LuaDebuggerOptions { PauseMode = mode });
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);
        state.Environment["finish"] = new LuaFunction(
            (context, token) =>
            {
                if (outcome == "error")
                    throw new InvalidOperationException("invocation failed");
                if (outcome == "cancel")
                {
                    lifetime.Cancel();
                    token.ThrowIfCancellationRequested();
                }
                return new(context.Return(42));
            }
        );
        var next = state.Load("local x=40\nreturn x+2", "=next");
        debugger.RegisterSource(next);
        debugger.SetBreakpoints("=next", [2]);
        var stops = 0;
        debugger.Stopped += stop =>
            debugger.Continue(stop.Id, stops++ == 0 ? step : LuaDebugStep.Continue);
        debugger.Attach();
        debugger.Pause();
        if (outcome == "return")
            Assert.That(
                await state.DoStringAsync("return 42", "=entry", lifetime.Token),
                Is.EqualTo(new LuaValue[] { 42 })
            );
        else if (outcome == "error")
            Assert.ThrowsAsync<LuaRuntimeException>(async () =>
                await state.DoStringAsync("return finish()", "=entry", lifetime.Token)
            );
        else
            Assert.ThrowsAsync<LuaCanceledException>(async () =>
                await state.DoStringAsync("return finish()", "=entry", lifetime.Token)
            );
        Assert.That(
            typeof(LuaDebugger)
                .GetField("step", BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(debugger),
            Is.Null
        );
        Assert.That(
            await state.ExecuteAsync(next, timeout.Token),
            Is.EqualTo(new LuaValue[] { 42 })
        );
        Assert.That(stops, Is.EqualTo(2));
    }

    [Test]
    public async Task StepOverSurvivesADirectCoroutineYield()
    {
        using var state = LuaState.Create();
        state.OpenStandardLibraries();
        using var debugger = new LuaDebugger(state);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var closure = state.Load("local x=1\ncoroutine.yield(x)\nx=x+1\nreturn x", "=yield-step");
        using var thread = state.CreateCoroutine(closure);
        var stops = Channel.CreateUnbounded<LuaDebugStop>();
        debugger.Stopped += stop => stops.Writer.TryWrite(stop);
        debugger.RegisterSource(closure);
        debugger.SetBreakpoints("=yield-step", [2]);
        debugger.Attach();
        var output = new LuaStack();
        var execution = Task.Run(async () => await thread.ResumeAsync(output, timeout.Token));
        try
        {
            var first = await stops.Reader.ReadAsync(timeout.Token);
            Assert.That(first.Line, Is.EqualTo(2));
            debugger.Continue(first.Id, LuaDebugStep.Over);
            await execution.WaitAsync(timeout.Token);
            Assert.That(output.AsSpan().ToArray(), Is.EqualTo(new LuaValue[] { true, 1 }));
            output.Clear();
            execution = Task.Run(async () => await thread.ResumeAsync(output, timeout.Token));
            var second = await stops.Reader.ReadAsync(timeout.Token);
            Assert.That(second.Line, Is.EqualTo(3));
            Assert.That(second.Reason, Is.EqualTo("step"));
            debugger.Continue(second.Id);
            await execution.WaitAsync(timeout.Token);
            Assert.That(output.AsSpan().ToArray(), Is.EqualTo(new LuaValue[] { true, 2 }));
        }
        finally
        {
            debugger.Detach();
            await execution;
        }
    }

    [TestCase(false)]
    [TestCase(true)]
    public void ImmediateSynchronousResumeDrainsAcceptedCallbacks(bool throwPosted)
    {
        using var state = LuaState.Create();
        using var debugger = new LuaDebugger(
            state,
            new LuaDebuggerOptions { PauseMode = LuaDebugPauseMode.Synchronous }
        );
        var previous = SynchronizationContext.Current;
        var thread = Environment.CurrentManagedThreadId;
        var continuation = false;
        var continuationThread = 0;
        var posted = false;
        debugger.Stopped += async _ =>
        {
            await Task.Yield();
            continuationThread = Environment.CurrentManagedThreadId;
            continuation = true;
        };
        debugger.Stopped += stop =>
        {
            SynchronizationContext.Current!.Post(
                _ =>
                {
                    posted = true;
                    if (throwPosted)
                        throw new InvalidOperationException("posted callback failed");
                },
                null
            );
            debugger.Continue(stop.Id);
        };
        debugger.Attach();
        debugger.Pause();
        if (throwPosted)
        {
            var failure = Assert.Throws<LuaRuntimeException>(() =>
                state.DoStringAsync("return 42").GetAwaiter().GetResult()
            );
            Assert.That(failure!.ToString(), Does.Contain("posted callback failed"));
        }
        else
            Assert.That(
                state.DoStringAsync("return 42").GetAwaiter().GetResult(),
                Is.EqualTo(new LuaValue[] { 42 })
            );
        Assert.That(continuation, Is.True);
        Assert.That(continuationThread, Is.EqualTo(thread));
        Assert.That(posted, Is.True);
        Assert.That(debugger.CurrentStop, Is.Null);
        Assert.That(SynchronizationContext.Current, Is.SameAs(previous));
        Assert.That(
            state.DoStringAsync("return 43").GetAwaiter().GetResult(),
            Is.EqualTo(new LuaValue[] { 43 })
        );
    }

    [TestCase("return", false, "run")]
    [TestCase("error", false, "run")]
    [TestCase("cancel", false, "run")]
    [TestCase("return", true, "run")]
    [TestCase("return", true, "call")]
    [TestCase("return", true, "index")]
    [TestCase("return", true, "newindex")]
    [TestCase("return", true, "add")]
    public async Task AttachmentRejectsActiveHostExecutionAndAllowsReuse(
        string outcome,
        bool coroutine,
        string entry
    )
    {
        using var state = LuaState.Create();
        state.OpenStandardLibraries();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var lifetime = new CancellationTokenSource();
        using var release = new ManualResetEventSlim();
        var entered = Signal();
        state.SetHook(
            new LuaFunction(
                (context, _) =>
                {
                    entered.TrySetResult(true);
                    release.Wait(timeout.Token);
                    return new(context.Return());
                }
            ),
            "",
            1
        );
        using var thread = coroutine
            ? state.CreateCoroutine(state.Load("return 7", "=entry"))
            : state.CreateThread();
        using var debugger = new LuaDebugger(thread);
        var closure = thread.Load(outcome == "error" ? "error('failed')" : "return 55", "=child");
        Exception? failure = null;
        LuaValue[]? results = null;
        var execution = Task.Run(async () =>
        {
            try
            {
                if (entry == "run")
                    results = await thread.ExecuteAsync(closure, lifetime.Token);
                else if (entry == "call")
                    results = await thread.CallAsync(closure, [], lifetime.Token);
                else
                {
                    var table = new LuaTable
                    {
                        Metatable = new LuaTable { ["__" + entry] = closure },
                    };
                    switch (entry)
                    {
                        case "index":
                            results = [await thread.GetTableAsync(table, "x", lifetime.Token)];
                            break;
                        case "newindex":
                            await thread.SetTableAsync(table, "x", 1, lifetime.Token);
                            results = [];
                            break;
                        case "add":
                            results = [await thread.AddAsync(table, 1, lifetime.Token)];
                            break;
                    }
                }
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        try
        {
            await entered.Task.WaitAsync(timeout.Token);
            Assert.That(state.IsRunning, Is.False);
            Assert.That(thread.IsRunning, Is.True);
            Assert.Throws<InvalidOperationException>(debugger.Attach);
            if (outcome == "cancel")
                lifetime.Cancel();
        }
        finally
        {
            release.Set();
            await execution.WaitAsync(timeout.Token);
        }
        if (outcome == "return")
        {
            Assert.That(failure, Is.Null);
            Assert.That(results, Is.EqualTo(entry == "newindex" ? [] : new LuaValue[] { 55 }));
        }
        else
            Assert.That(
                failure,
                outcome == "cancel"
                    ? Is.InstanceOf<OperationCanceledException>()
                    : Is.TypeOf<LuaRuntimeException>()
            );
        debugger.Attach();
        var stops = 0;
        debugger.Stopped += stop =>
        {
            stops++;
            debugger.Continue(stop.Id);
        };
        debugger.Pause();
        Assert.That(
            await thread.DoStringAsync("return 42", cancellationToken: timeout.Token),
            Is.EqualTo(new LuaValue[] { 42 })
        );
        Assert.That(stops, Is.EqualTo(1));
        if (coroutine)
        {
            var output = new LuaStack();
            await thread.ResumeAsync(output, timeout.Token);
            Assert.That(output.AsSpan().ToArray(), Is.EqualTo(new LuaValue[] { true, 7 }));
        }
    }

    [TestCase(LuaDebugPauseMode.Asynchronous)]
    [TestCase(LuaDebugPauseMode.Synchronous)]
    public async Task DisposingAnActiveStopNotifiesTheHostAfterInspectionDrains(
        LuaDebugPauseMode mode
    )
    {
        using var state = LuaState.Create();
        using var debugger = new LuaDebugger(state, new LuaDebuggerOptions { PauseMode = mode });
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var stopped = new TaskCompletionSource<LuaDebugStop>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        var entered = Signal();
        var cleanupEntered = Signal();
        var cleanupRelease = Signal();
        var drained = false;
        var applicationPaused = false;
        var ended = new List<int>();
        debugger.Stopped += stop =>
        {
            applicationPaused = true;
            stopped.SetResult(stop);
        };
        debugger.StopEnded += id =>
        {
            Assert.That(drained, Is.True);
            Assert.That(debugger.CurrentStop, Is.Null);
            applicationPaused = false;
            ended.Add(id);
        };
        state.Environment["inspect"] = new LuaFunction(
            async (context, token) =>
            {
                try
                {
                    entered.TrySetResult(true);
                    await new TaskCompletionSource<bool>().Task.WaitAsync(token);
                    return context.Return();
                }
                finally
                {
                    cleanupEntered.TrySetResult(true);
                    await cleanupRelease.Task.WaitAsync(timeout.Token);
                    drained = true;
                }
            }
        );
        debugger.Attach();
        debugger.Pause();
        var execution = Task.Run(async () =>
            await state.DoStringAsync("return 42", cancellationToken: timeout.Token)
        );
        try
        {
            var stop = await stopped.Task.WaitAsync(timeout.Token);
            var inspection = debugger.EvaluateAsync(stop.Id, 1, "inspect()", timeout.Token);
            await entered.Task.WaitAsync(timeout.Token);
            debugger.Dispose();
            await cleanupEntered.Task.WaitAsync(timeout.Token);
            Assert.That(applicationPaused, Is.True);
            Assert.That(ended, Is.Empty);
            cleanupRelease.TrySetResult(true);
            Assert.ThrowsAsync<TaskCanceledException>(async () => await inspection);
            Assert.That(
                await execution.WaitAsync(timeout.Token),
                Is.EqualTo(new LuaValue[] { 42 })
            );
            Assert.That(applicationPaused, Is.False);
            Assert.That(ended, Is.EqualTo(new[] { stop.Id }));
            Assert.That(debugger.IsAttached, Is.False);
            Assert.Throws<ObjectDisposedException>(debugger.Attach);
        }
        finally
        {
            cleanupRelease.TrySetResult(true);
            debugger.Detach();
            await execution;
        }
    }

    [TestCase("return")]
    [TestCase("yield")]
    [TestCase("error")]
    public async Task AttachmentRejectsActiveDirectResumesAndAllowsIdleThreads(string outcome)
    {
        using var state = LuaState.Create();
        state.OpenStandardLibraries();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var release = new ManualResetEventSlim();
        var entered = Signal();
        state.SetHook(
            new LuaFunction(
                (context, _) =>
                {
                    entered.TrySetResult(true);
                    release.Wait(timeout.Token);
                    return new(context.Return());
                }
            ),
            "",
            1
        );
        var source = outcome switch
        {
            "yield" => "coroutine.yield(55)\nreturn 42",
            "error" => "error('failed')",
            _ => "local n=0\nfor i=1,10 do n=n+i end\nreturn n",
        };
        using var thread = state.CreateCoroutine(
            state.Load(source, "=direct-resume"),
            isProtectedMode: true
        );
        using var debugger = new LuaDebugger(state);
        var output = new LuaStack();
        var execution = Task.Run(async () => await thread.ResumeAsync(output, timeout.Token));
        try
        {
            await entered.Task.WaitAsync(timeout.Token);
            Assert.That(state.IsRunning, Is.False);
            Assert.That(thread.IsRunning, Is.True);
            Assert.Throws<InvalidOperationException>(debugger.Attach);
        }
        finally
        {
            release.Set();
            await execution.WaitAsync(timeout.Token);
        }
        Assert.That(output.AsSpan()[0], Is.EqualTo(new LuaValue(outcome != "error")));
        debugger.Attach();
        if (outcome == "yield")
        {
            var stops = 0;
            debugger.Stopped += stop =>
            {
                stops++;
                debugger.Continue(stop.Id);
            };
            debugger.Pause();
            var results = new LuaStack();
            await thread.ResumeAsync(results, timeout.Token);
            Assert.That(results.AsSpan().ToArray(), Is.EqualTo(new LuaValue[] { true, 42 }));
            Assert.That(stops, Is.EqualTo(1));
        }
    }

    [TestCase(LuaDebugPauseMode.Asynchronous)]
    [TestCase(LuaDebugPauseMode.Synchronous)]
    public async Task ReattachingTheSameDebuggerDoesNotSkipTheFirstCheckpoint(
        LuaDebugPauseMode mode
    )
    {
        using var state = LuaState.Create();
        using var debugger = new LuaDebugger(state, new LuaDebuggerOptions { PauseMode = mode });
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var stops = Channel.CreateUnbounded<LuaDebugStop>();
        debugger.Stopped += stop => stops.Writer.TryWrite(stop);
        var closure = state.Load("local x=42\nreturn x", "=reattach");
        debugger.Attach();
        debugger.Pause();
        var execution = Task.Run(async () => await state.ExecuteAsync(closure, timeout.Token));
        try
        {
            var first = await stops.Reader.ReadAsync(timeout.Token);
            Assert.That(first.Line, Is.EqualTo(1));
            debugger.Detach();
            Assert.That(
                await execution.WaitAsync(timeout.Token),
                Is.EqualTo(new LuaValue[] { 42 })
            );
            debugger.Attach();
            debugger.Pause();
            execution = Task.Run(async () => await state.ExecuteAsync(closure, timeout.Token));
            var second = await stops.Reader.ReadAsync(timeout.Token);
            Assert.That(second.Line, Is.EqualTo(1));
            debugger.Continue(second.Id);
            Assert.That(
                await execution.WaitAsync(timeout.Token),
                Is.EqualTo(new LuaValue[] { 42 })
            );
        }
        finally
        {
            debugger.Detach();
            await execution;
        }
    }

    [TestCase(LuaDebugPauseMode.Asynchronous)]
    [TestCase(LuaDebugPauseMode.Synchronous)]
    public async Task YieldedCoroutineUsesTheCurrentDebugger(LuaDebugPauseMode mode)
    {
        using var state = LuaState.Create();
        state.OpenStandardLibraries();
        using var first = new LuaDebugger(state);
        using var second = new LuaDebugger(state, new LuaDebuggerOptions { PauseMode = mode });
        first.Attach();
        var closure = state.Load(
            "co=coroutine.create(function()\n coroutine.yield(1)\n local x=42\n return x\nend)",
            "@lifetime.lua"
        );
        await state.ExecuteAsync(closure);
        Assert.That(
            await state.DoStringAsync("return coroutine.resume(co)"),
            Is.EqualTo(new LuaValue[] { true, 1 })
        );
        first.Detach();
        second.Attach();
        second.RegisterSource(closure);
        Assert.That(second.SetBreakpoints("lifetime.lua", [4])[0].Verified, Is.True);
        var stops = new List<int>();
        second.Stopped += stop =>
        {
            stops.Add(stop.Line);
            second.Continue(stop.Id);
        };
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        Assert.That(
            await state.DoStringAsync(
                "return coroutine.resume(co)",
                cancellationToken: timeout.Token
            ),
            Is.EqualTo(new LuaValue[] { true, 42 })
        );
        Assert.That(stops, Is.EqualTo(new[] { 4 }));
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task SynchronousCallbackFailureDrainsInspectionBeforeExecutionUnwinds(
        bool throwOnCancellation
    )
    {
        using var state = LuaState.Create();
        using var debugger = new LuaDebugger(
            state,
            new LuaDebuggerOptions { PauseMode = LuaDebugPauseMode.Synchronous }
        );
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var stopped = new TaskCompletionSource<LuaDebugStop>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        var entered = Signal();
        var drained = false;
        var ended = false;
        var pumped = false;
        SynchronizationContext? pauseContext = null;
        debugger.Stopped += stop =>
        {
            pauseContext = SynchronizationContext.Current;
            stopped.SetResult(stop);
        };
        debugger.StopEnded += _ => ended = true;
        state.Environment["inspect"] = new LuaFunction(
            async (context, token) =>
            {
                using var registration = token.Register(() =>
                {
                    if (throwOnCancellation)
                        throw new InvalidOperationException("cancellation callback failed");
                });
                try
                {
                    entered.SetResult(true);
                    await Task.Delay(Timeout.Infinite, token);
                    return context.Return();
                }
                finally
                {
                    drained = true;
                }
            }
        );
        debugger.Attach();
        debugger.Pause();
        var execution = Task.Run(async () =>
            await state.DoStringAsync("return 42", cancellationToken: timeout.Token)
        );
        try
        {
            var stop = await stopped.Task.WaitAsync(timeout.Token);
            var inspection = debugger.EvaluateAsync(stop.Id, 1, "inspect()", timeout.Token);
            await entered.Task.WaitAsync(timeout.Token);
            pauseContext!.Post(
                _ =>
                {
                    pauseContext.Post(_ => pumped = true, null);
                    throw new InvalidOperationException("posted callback failed");
                },
                null
            );
            try
            {
                await execution.WaitAsync(timeout.Token);
                Assert.Fail("The callback failure should fault Lua execution.");
            }
            catch (LuaRuntimeException exception)
            {
                Assert.That(exception.ToString(), Does.Contain("posted callback failed"));
            }
            Assert.That(drained, Is.True);
            Assert.That(pumped, Is.True);
            Assert.That(ended, Is.True);
            Assert.That(debugger.CurrentStop, Is.Null);
            Assert.ThrowsAsync<TaskCanceledException>(async () => await inspection);
            Assert.That(await state.DoStringAsync("return 43"), Is.EqualTo(new LuaValue[] { 43 }));
        }
        finally
        {
            debugger.Detach();
        }
    }

    [TestCase(LuaDebugPauseMode.Asynchronous)]
    [TestCase(LuaDebugPauseMode.Synchronous)]
    public async Task PausePolicyPreservesInspectionAffinityAndControlsHostProgress(
        LuaDebugPauseMode mode
    )
    {
        using var state = LuaState.Create();
        using var debugger = new LuaDebugger(state, new LuaDebuggerOptions { PauseMode = mode });
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var stops = Channel.CreateUnbounded<LuaDebugStop>();
        debugger.Stopped += stop => stops.Writer.TryWrite(stop);
        var entered = Signal();
        var release = Signal();
        var returned = Signal();
        var progress = Signal();
        var before = 0;
        var after = 0;
        state.Environment["inspect"] = new LuaFunction(
            async (context, token) =>
            {
                before = Environment.CurrentManagedThreadId;
                entered.TrySetResult(true);
                await release.Task.WaitAsync(token);
                after = Environment.CurrentManagedThreadId;
                return context.Return(42);
            }
        );
        debugger.Attach();
        debugger.Pause();
        var host = new TestHost();
        var execution = host.RunAsync(async () =>
        {
            var original = SynchronizationContext.Current;
            var operation = state.DoStringAsync(
                "local x=40\nreturn x+2",
                cancellationToken: timeout.Token
            );
            returned.TrySetResult(true);
            Assert.That(await operation, Is.EqualTo(new LuaValue[] { 42 }));
            Assert.That(SynchronizationContext.Current, Is.SameAs(original));
        });
        try
        {
            var stop = await stops.Reader.ReadAsync(timeout.Token);
            host.Post(_ => progress.TrySetResult(true), null);
            var evaluation = debugger.EvaluateAsync(stop.Id, 1, "inspect()", timeout.Token);
            await entered.Task.WaitAsync(timeout.Token);
            release.TrySetResult(true);
            Assert.That((await evaluation).Value, Is.EqualTo(new LuaValue(42)));
            Assert.That(before, Is.EqualTo(host.ThreadId));
            Assert.That(after, Is.EqualTo(host.ThreadId));
            if (mode == LuaDebugPauseMode.Asynchronous)
            {
                await returned.Task.WaitAsync(timeout.Token);
                await progress.Task.WaitAsync(timeout.Token);
            }
            else
            {
                Assert.That(returned.Task.IsCompleted, Is.False);
                Assert.That(progress.Task.IsCompleted, Is.False);
            }
            debugger.Continue(stop.Id);
            await execution.WaitAsync(timeout.Token);
        }
        finally
        {
            debugger.Detach();
            await execution;
        }
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task BlockingPauseReleasesOnDetachOrCancellation(bool cancel)
    {
        using var state = LuaState.Create();
        using var debugger = new LuaDebugger(
            state,
            new LuaDebuggerOptions { PauseMode = LuaDebugPauseMode.Synchronous }
        );
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var lifetime = new CancellationTokenSource();
        var stops = Channel.CreateUnbounded<LuaDebugStop>();
        debugger.Stopped += stop => stops.Writer.TryWrite(stop);
        debugger.Attach();
        debugger.Pause();
        var host = new TestHost();
        var execution = host.RunAsync(async () =>
        {
            try
            {
                var results = await state.DoStringAsync(
                    "return 42",
                    cancellationToken: lifetime.Token
                );
                Assert.That(cancel, Is.False);
                Assert.That(results, Is.EqualTo(new LuaValue[] { 42 }));
            }
            catch (LuaCanceledException) when (cancel) { }
        });
        await stops.Reader.ReadAsync(timeout.Token);
        if (cancel)
            lifetime.Cancel();
        else
            debugger.Detach();
        await execution.WaitAsync(timeout.Token);
        Assert.That(debugger.CurrentStop, Is.Null);
        debugger.Detach();
        Assert.That(await state.DoStringAsync("return 43"), Is.EqualTo(new LuaValue[] { 43 }));
    }

    [Test]
    public async Task ApplicationCanMapLogicalSourcesAndSupplyTheExecutedText()
    {
        var path = Path.GetFullPath("user-scripts/player.lua");
        var text = "local x=40\nreturn x+2";
        using var state = LuaState.Create();
        using var debugger = new LuaDebugger(
            state,
            new LuaDebuggerOptions
            {
                SourcePathResolver = name => name == "=uploaded:player" ? path : null,
                SourceTextProvider = (source, _) => new(source.Path == path ? text : null),
            }
        );
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var stops = Channel.CreateUnbounded<LuaDebugStop>();
        debugger.Stopped += stop => stops.Writer.TryWrite(stop);
        debugger.Attach();
        debugger.SetBreakpoints(path, [2]);
        var execution = state.DoStringAsync(text, "=uploaded:player", timeout.Token).AsTask();
        var stop = await stops.Reader.ReadAsync(timeout.Token);
        Assert.That(stop.Source.Path, Is.EqualTo(path));
        Assert.That(stop.Line, Is.EqualTo(2));
        Assert.That(
            await debugger.ReadSourceAsync(stop.Source.Id, timeout.Token),
            Is.EqualTo(text)
        );
        debugger.Continue(stop.Id);
        Assert.That(await execution, Is.EqualTo(new LuaValue[] { 42 }));
    }

    static TaskCompletionSource<bool> Signal() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    sealed class TestHost : SynchronizationContext
    {
        readonly BlockingCollection<(SendOrPostCallback Callback, object? State)> work = new();
        public int ThreadId { get; private set; }

        public Task RunAsync(Func<Task> action)
        {
            var completed = Signal();
            new Thread(() =>
            {
                ThreadId = Environment.CurrentManagedThreadId;
                SetSynchronizationContext(this);
                try
                {
                    var task = action();
                    _ = task.ContinueWith(
                        _ => work.CompleteAdding(),
                        CancellationToken.None,
                        TaskContinuationOptions.ExecuteSynchronously,
                        TaskScheduler.Default
                    );
                    foreach (var item in work.GetConsumingEnumerable())
                        item.Callback(item.State);
                    task.GetAwaiter().GetResult();
                    completed.TrySetResult(true);
                }
                catch (Exception exception)
                {
                    completed.TrySetException(exception);
                }
                finally
                {
                    SetSynchronizationContext(null);
                    work.Dispose();
                }
            })
            {
                IsBackground = true,
            }.Start();
            return completed.Task;
        }

        public override void Post(SendOrPostCallback callback, object? state) =>
            work.Add((callback, state));
    }
}
