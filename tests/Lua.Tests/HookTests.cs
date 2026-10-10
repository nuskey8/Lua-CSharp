namespace Lua.Tests;

public class HookTests
{
    const string Loop = "local x=0 for i=1,100 do x=x+i end return x";

    [TestCase(false, "runtime")]
    [TestCase(true, "runtime")]
    [TestCase(false, "host")]
    [TestCase(true, "host")]
    [TestCase(false, "cancel")]
    [TestCase(true, "cancel")]
    public async Task CountHook_AfterException_StateCanRunHooksAgain(
        bool asynchronous,
        string failure
    )
    {
        using var state = LuaState.Create();
        using var cancellation = new CancellationTokenSource();
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        state.SetHook(
            new("abort", (c, ct) => asynchronous ? ThrowAsync(c, ct) : Throw(c, ct)),
            "",
            4
        );

        var execution = state.DoStringAsync(Loop, "hook.lua", cancellation.Token).AsTask();
        if (asynchronous)
        {
            Assert.That(execution.IsCompleted, Is.False);
            gate.SetResult();
        }

        if (failure == "cancel")
        {
            Assert.ThrowsAsync<LuaCanceledException>(async () => await execution);
        }
        else
        {
            Assert.ThrowsAsync<LuaRuntimeException>(async () => await execution);
        }

        Assert.That(state.CallStackFrameCount, Is.Zero);
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

    [Test]
    public async Task CountHook_WhenFrameLimitIsExceeded_StateCanRunHooksAgain()
    {
        using var state = LuaState.Create();
        state.MaxCallDepth = 1;
        state.SetHook(new((c, ct) => new(c.Return())), "", 4);
        Assert.ThrowsAsync<LuaRuntimeException>(async () => await state.DoStringAsync(Loop));

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
