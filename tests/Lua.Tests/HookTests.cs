using Lua.Standard;

namespace Lua.Tests;

public class HookTests
{
    const string Loop = "local x=0 for i=1,100 do x=x+i end return x";

    [Test]
    [Combinatorial]
    public async Task Hook_AfterException_StateCanRunHooksAgain(
        [Values(false, true)] bool asynchronous,
        [Values("runtime", "host", "cancel")] string failure,
        [Values("count", "line", "call", "return", "sethook-return")] string hookEvent
    )
    {
        using var state = LuaState.Create();
        using var cancellation = new CancellationTokenSource();
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var hook = new LuaFunction(
            "abort",
            (c, ct) => asynchronous ? ThrowAsync(c, ct) : Throw(c, ct)
        );
        var source = Loop;
        if (hookEvent == "sethook-return")
        {
            state.OpenDebugLibrary();
            state.Environment["abort"] = hook;
            source = "debug.sethook(abort, 'r') " + Loop;
        }
        else
        {
            state.SetHook(hook, GetMask(hookEvent), hookEvent == "count" ? 4 : 0);
        }

        var execution = state.DoStringAsync(source, "hook.lua", cancellation.Token).AsTask();
        if (asynchronous)
        {
            Assert.That(execution.IsCompleted, Is.False);
            gate.SetResult();
        }

        if (failure == "cancel")
        {
            if (hookEvent is "call" or "return")
            {
                Assert.ThrowsAsync<OperationCanceledException>(async () => await execution);
            }
            else
            {
                Assert.ThrowsAsync<LuaCanceledException>(async () => await execution);
            }
        }
        else if (failure == "host" && hookEvent is "call" or "return")
        {
            Assert.ThrowsAsync<InvalidOperationException>(async () => await execution);
        }
        else
        {
            Assert.ThrowsAsync<LuaRuntimeException>(async () => await execution);
        }

        Assert.That(state.CallStackFrameCount, Is.Zero);
        Assert.That(state.NonYieldableCallCount, Is.Zero);
        var stackTop = state.Stack.Count;

        state.Environment["echo"] = new LuaFunction((c, ct) => new(c.Return(c.GetArgument(0))));
        for (var attempt = 0; attempt < 2; attempt++)
        {
            if (attempt == 1)
            {
                state.SetHook(null, "");
            }

            var events = new HashSet<string>();
            state.SetHook(
                new(
                    "reuse",
                    (c, ct) =>
                    {
                        events.Add(c.GetArgument<string>(0));
                        return new(c.Return());
                    }
                ),
                "lcr",
                4
            );

            var result = await state.DoStringAsync(
                "local x=0 for i=1,100 do x=x+i end echo(x) return x"
            );
            Assert.That(result[0], Is.EqualTo((LuaValue)5050));
            Assert.That(events, Is.SupersetOf(new[] { "count", "line", "call", "return" }));
            Assert.That(state.CallStackFrameCount, Is.Zero);
            Assert.That(state.Stack.Count, Is.EqualTo(stackTop));
        }

        ValueTask<int> Throw(LuaFunctionExecutionContext context, CancellationToken ct)
        {
            if (failure == "cancel")
            {
                cancellation.Cancel();
                ct.ThrowIfCancellationRequested();
            }

            throw failure switch
            {
                "host" => new InvalidOperationException("hook failed"),
                _ => new LuaRuntimeException(context.State, "hook failed"),
            };
        }

        async ValueTask<int> ThrowAsync(LuaFunctionExecutionContext context, CancellationToken ct)
        {
            await gate.Task;
            return await Throw(context, ct);
        }
    }

    [TestCase("count", 1)]
    [TestCase("line", 1)]
    [TestCase("call", 1)]
    [TestCase("return", 1)]
    [TestCase("sethook-return", 2)]
    public async Task Hook_WhenFrameLimitIsExceeded_StateCanRunHooksAgain(
        string hookEvent,
        int depth
    )
    {
        using var state = LuaState.Create();
        state.MaxCallDepth = depth;
        var abortedFires = 0;
        var hook = new LuaFunction(
            (c, ct) =>
            {
                abortedFires++;
                return new(c.Return());
            }
        );
        var source = Loop;
        if (hookEvent == "sethook-return")
        {
            state.OpenDebugLibrary();
            state.Environment["abort"] = hook;
            source = "debug.sethook(abort, 'r') " + Loop;
        }
        else
        {
            state.SetHook(hook, GetMask(hookEvent), hookEvent == "count" ? 4 : 0);
        }

        if (hookEvent is "call" or "return")
        {
            Assert.ThrowsAsync<LuaStackOverflowException>(async () =>
                await state.DoStringAsync(source)
            );
        }
        else
        {
            Assert.ThrowsAsync<LuaRuntimeException>(async () => await state.DoStringAsync(source));
        }
        Assert.That(abortedFires, Is.Zero);
        Assert.That(state.CallStackFrameCount, Is.Zero);
        var stackTop = state.Stack.Count;

        state.MaxCallDepth = 10;
        var fires = 0;
        state.SetHook(
            new(
                (c, ct) =>
                {
                    fires++;
                    return new(c.Return());
                }
            ),
            "",
            4
        );
        var result = await state.DoStringAsync(Loop);
        Assert.That(result[0], Is.EqualTo((LuaValue)5050));
        Assert.That(fires, Is.GreaterThan(0));
        Assert.That(state.CallStackFrameCount, Is.Zero);
        Assert.That(state.Stack.Count, Is.EqualTo(stackTop));
    }

    static string GetMask(string hookEvent) =>
        hookEvent switch
        {
            "line" => "l",
            "call" => "c",
            "return" => "r",
            _ => "",
        };

    [TestCase(false)]
    [TestCase(true)]
    public async Task DebugSetHook_InsideHook_DoesNotRecursivelyFireReturnHook(bool asynchronous)
    {
        using var state = LuaState.Create();
        state.OpenDebugLibrary();
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var replacementFires = 0;
        state.Environment["replacement"] = new LuaFunction(
            (c, ct) =>
            {
                replacementFires++;
                return new(c.Return());
            }
        );
        state.Environment["echo"] = new LuaFunction((c, ct) => new(c.Return(c.GetArgument(0))));
        var originalFires = 0;
        state.SetHook(
            new(
                async (c, ct) =>
                {
                    originalFires++;
                    if (asynchronous)
                    {
                        await gate.Task;
                    }
                    var result = await state.DoStringAsync(
                        "debug.sethook(replacement, 'r') return 7",
                        "rearm.lua",
                        ct
                    );
                    Assert.That(result[0], Is.EqualTo((LuaValue)7));
                    Assert.That(replacementFires, Is.Zero);
                    return c.Return();
                }
            ),
            "",
            4
        );

        var execution = state
            .DoStringAsync("local x=0 for i=1,100 do x=x+i end echo(x) return x")
            .AsTask();
        if (asynchronous)
        {
            Assert.That(execution.IsCompleted, Is.False);
            gate.SetResult();
        }
        var result = await execution;
        Assert.That(result[0], Is.EqualTo((LuaValue)5050));
        Assert.That(originalFires, Is.EqualTo(1));
        Assert.That(replacementFires, Is.GreaterThan(0));
        Assert.That(state.CallStackFrameCount, Is.Zero);
        Assert.That(state.Stack.Count, Is.Zero);
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task CountHook_CanChangeIntervalInsideCallback(bool asynchronous)
    {
        using var state = LuaState.Create();
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var fires = 0;
        LuaFunction hook = null!;
        hook = new(
            "adaptive",
            async (c, ct) =>
            {
                fires++;
                if (fires == 1)
                {
                    if (asynchronous)
                    {
                        await gate.Task;
                    }

                    state.SetHook(hook, "", 64);
                }

                return c.Return();
            }
        );
        state.SetHook(hook, "", 4);
        var execution = state.DoStringAsync(Loop).AsTask();
        if (asynchronous)
        {
            Assert.That(execution.IsCompleted, Is.False);
            gate.SetResult();
        }

        var result = await execution;
        Assert.That(result[0], Is.EqualTo((LuaValue)5050));
        Assert.That(fires, Is.EqualTo(4));
    }

    [Test]
    public async Task CountHook_CanDisableItselfInsideCallback()
    {
        using var state = LuaState.Create();
        var fires = 0;
        state.SetHook(
            new(
                (c, ct) =>
                {
                    fires++;
                    state.SetHook(null, "");
                    return new(c.Return());
                }
            ),
            "",
            4
        );
        var result = await state.DoStringAsync(Loop);
        Assert.That(result[0], Is.EqualTo((LuaValue)5050));
        Assert.That(fires, Is.EqualTo(1));
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task CountHook_CanReplaceItselfInsideCallback(bool asynchronous)
    {
        using var state = LuaState.Create();
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var originalFires = 0;
        var replacementFires = 0;
        var replacement = new LuaFunction(
            (c, ct) =>
            {
                replacementFires++;
                return new(c.Return());
            }
        );
        state.SetHook(
            new(
                async (c, ct) =>
                {
                    originalFires++;
                    if (asynchronous)
                    {
                        await gate.Task;
                    }

                    state.SetHook(replacement, "", 64);
                    return c.Return();
                }
            ),
            "",
            4
        );
        var execution = state.DoStringAsync(Loop).AsTask();
        if (asynchronous)
        {
            Assert.That(execution.IsCompleted, Is.False);
            gate.SetResult();
        }

        var result = await execution;
        Assert.That(result[0], Is.EqualTo((LuaValue)5050));
        Assert.That(originalFires, Is.EqualTo(1));
        Assert.That(replacementFires, Is.EqualTo(3));
    }

    [Test]
    public async Task CountHook_DoesNotRecursivelyFireForLuaCalledByHook()
    {
        using var state = LuaState.Create();
        var fires = 0;
        state.SetHook(
            new(
                async (c, ct) =>
                {
                    fires++;
                    if (fires == 1)
                    {
                        var result = await state.DoStringAsync(Loop, "hook-body.lua", ct);
                        Assert.That(result[0], Is.EqualTo((LuaValue)5050));
                        Assert.That(fires, Is.EqualTo(1));
                    }

                    return c.Return();
                }
            ),
            "",
            4
        );
        var result = await state.DoStringAsync(Loop);
        Assert.That(result[0], Is.EqualTo((LuaValue)5050));
        Assert.That(fires, Is.EqualTo(51));
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task CountHook_DoesNotFireInsideHostFunction(bool asynchronous)
    {
        using var state = LuaState.Create();
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var fires = 0;
        var firesAtEntry = -1;
        var firesAtExit = -1;
        state.SetHook(
            new(
                (c, ct) =>
                {
                    fires++;
                    return new(c.Return());
                }
            ),
            "",
            1
        );
        state.Environment["host"] = new LuaFunction(
            async (c, ct) =>
            {
                firesAtEntry = fires;
                if (asynchronous)
                {
                    await gate.Task;
                }
                firesAtExit = fires;
                return c.Return();
            }
        );

        var execution = state
            .DoStringAsync("host() local x=0 for i=1,100 do x=x+i end return x")
            .AsTask();
        Assert.That(firesAtEntry, Is.GreaterThanOrEqualTo(0));
        if (asynchronous)
        {
            Assert.That(execution.IsCompleted, Is.False);
            Assert.That(fires, Is.EqualTo(firesAtEntry));
            gate.SetResult();
        }
        var result = await execution;
        Assert.That(result[0], Is.EqualTo((LuaValue)5050));
        Assert.That(firesAtExit, Is.EqualTo(firesAtEntry));
        Assert.That(fires, Is.GreaterThan(firesAtExit));
    }
}
