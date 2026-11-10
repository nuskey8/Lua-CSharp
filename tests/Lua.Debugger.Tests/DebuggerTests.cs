using System.Threading.Channels;
using Lua.Debugging;
using Lua.Runtime;
using Lua.Standard;

namespace Lua.Debugger.Tests;

public class DebuggerTests
{
    [Test]
    public async Task NewlyRegisteredLinesUpdateExistingExecutionControls()
    {
        using var state = LuaState.Create();
        using var debugger = new LuaDebugger(state);
        debugger.Attach();
        var first = state.Load("\n\n\n\n\nreturn 6", "=shared-source");
        debugger.RegisterSource(first);
        Assert.That(debugger.SetBreakpoints("=shared-source", [3])[0].Line, Is.EqualTo(6));
        var stops = new List<int>();
        debugger.Stopped += stop =>
        {
            stops.Add(stop.Line);
            debugger.Continue(stop.Id);
        };
        await state.ExecuteAsync(first);
        var second = state.Load("\n\n\nreturn 4", "=shared-source");
        debugger.RegisterSource(second);
        await state.ExecuteAsync(first);
        await state.ExecuteAsync(second);
        Assert.That(stops, Is.EqualTo(new[] { 6, 4 }));
    }

    [TestCase("local n=0;for i=1,3 do n=n+i end;return n")]
    [TestCase("for i=1,3 do end; return 42")]
    [TestCase("for i=1,0 do error('unreachable') end; return 42")]
    [TestCase("local n=0;for i=1,3 do for j=1,2 do n=n+j end end;return n")]
    [TestCase("local n=0\nfor i=3,1,-1 do\n if i==2 then break end\n n=n+i\nend\nreturn n")]
    [TestCase("local n=0\nfor i=1,\n 3 do n=n+i end\nreturn n")]
    [TestCase("local n=0;while n<3 do n=n+1 end;repeat n=n-1 until n==0;return n")]
    [TestCase(
        "local function f(x) return x+1 end;local function g(x) return f(x) end;local n=0;for i=1,3 do n=g(n) end;return n"
    )]
    [TestCase("local n=0;for k,v in ipairs({1,2,3}) do n=n+v end;return n")]
    // Scoping and control flow that commonly diverges from a line hook.
    [TestCase("local n = 0\nrepeat\n  local done = n >= 2\n  n = n + 1\nuntil done\nreturn n")]
    [TestCase("local n = 0\n::again::\nn = n + 1\nif n < 3 then goto again end\nreturn n")]
    [TestCase(
        "local t = {value = 1}\nfunction t:bump(x)\n  self.value = self.value + x\n  return self.value\nend\nreturn t:bump(2)"
    )]
    [TestCase("local x = 1\nlocal y =\n  x\n  + 2\nreturn y")]
    [TestCase("--[[ a\nb ]]\nlocal s = [[line1\nline2]]\nreturn #s")]
    [TestCase(
        "local function make(a)\n  return function(b)\n    return a + b\n  end\nend\nreturn make(1)(2)"
    )]
    [TestCase(
        "local s = 0\nfor i = 1, 10 do\n  if i == 3 then break end\n  s = s + i\nend\nreturn s"
    )]
    public async Task BreakpointLineEventsMatchLuaLineHooks(string source)
    {
        async Task<int[]> Run(bool debug)
        {
            using var state = LuaState.Create();
            state.OpenStandardLibraries();
            using var debugger = new LuaDebugger(state);
            var lines = new List<int>();
            var closure = state.Load(source, "@line-events.lua");
            if (debug)
            {
                debugger.Attach();
                debugger.RegisterSource(closure);
                debugger.SetBreakpoints(
                    "line-events.lua",
                    Enumerable.Range(1, source.Count(c => c == '\n') + 1).ToArray()
                );
                debugger.Stopped += stop =>
                {
                    lines.Add(stop.Line);
                    debugger.Continue(stop.Id);
                };
            }
            else
                state.SetHook(
                    new LuaFunction(
                        (context, _) =>
                        {
                            lines.Add(context.GetArgument<int>(1));
                            return new(context.Return());
                        }
                    ),
                    "l"
                );
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await state.ExecuteAsync(closure, timeout.Token);
            return lines.ToArray();
        }
        Assert.That(await Run(true), Is.EqualTo(await Run(false)));
    }

    [Test]
    public async Task NumericLoopBreakpointsIncludeTheFirstIteration()
    {
        using var state = LuaState.Create();
        using var debugger = new LuaDebugger(state);
        state.Environment["n"] = 0;
        debugger.Attach();
        debugger.SetBreakpoints("numeric-loop.lua", [1]);
        var values = new List<double>();
        debugger.Stopped += stop =>
        {
            values.Add(state.Environment["n"].Read<double>());
            debugger.Continue(stop.Id);
        };
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        Assert.That(
            await state.DoStringAsync(
                "for i=1,3 do n=n+i end;return n",
                "@numeric-loop.lua",
                timeout.Token
            ),
            Is.EqualTo(new LuaValue[] { 6 })
        );
        Assert.That(values, Is.EqualTo(new double[] { 0, 0, 1, 3 }));
    }

    [Test]
    public async Task PauseDuringAnAwaitingMetamethodStopsBeforeTheChunkReturns()
    {
        var (state, debugger, stops) = Create();
        using var ownedState = state;
        using var ownedDebugger = debugger;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var started = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        var resume = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        state.Environment["t"] = new LuaTable
        {
            Metatable = new LuaTable
            {
                ["__add"] = new LuaFunction(
                    async (context, token) =>
                    {
                        started.TrySetResult(true);
                        await resume.Task.WaitAsync(token);
                        return context.Return(42);
                    }
                ),
            },
        };
        var execution = state
            .DoStringAsync("return t+t", cancellationToken: timeout.Token)
            .AsTask();
        await started.Task.WaitAsync(timeout.Token);
        debugger.Pause();
        resume.TrySetResult(true);
        var stop = await stops.Reader.ReadAsync(timeout.Token);
        Assert.That(stop.Reason, Is.EqualTo("pause"));
        Assert.That(execution.IsCompleted, Is.False);
        debugger.Continue(stop.Id);
        Assert.That(await execution, Is.EqualTo(new LuaValue[] { 42 }));
    }

    [Test]
    public async Task CSharpClosureUpValuesCanBeEdited()
    {
        var (state, debugger, stops) = Create();
        using var ownedState = state;
        using var ownedDebugger = debugger;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var leaf = state.Load("local x=0\nreturn", "=leaf");
        state.Environment["bridge"] = new CSharpClosure(
            "bridge",
            [7, 9],
            async (context, token) =>
            {
                await context.State.RunAsync(leaf, token);
                return context.Return(context.GetCsClosure()!.UpValues);
            }
        );
        debugger.SetBreakpoints("=leaf", [2]);
        var execution = state.DoStringAsync("return bridge()", "=main", timeout.Token).AsTask();
        var stop = await stops.Reader.ReadAsync(timeout.Token);
        var frame = (await debugger.GetStackTraceAsync(stop.Id, timeout.Token))
            .Single(frame => frame.Name == "bridge")
            .Id;
        var upvalues = await debugger.GetVariablesAsync(
            stop.Id,
            frame,
            LuaDebugScope.UpValues,
            timeout.Token
        );
        Assert.That(upvalues[1].Value, Is.EqualTo(new LuaValue(9)));
        await debugger.SetVariableAsync(
            stop.Id,
            frame,
            LuaDebugScope.UpValues,
            upvalues[1].Name,
            "42",
            timeout.Token
        );
        debugger.Continue(stop.Id);
        Assert.That(await execution, Is.EqualTo(new LuaValue[] { 7, 42 }));
    }

    [Test]
    public async Task InspectionUsesTheInnermostLocalEnvironment()
    {
        var (state, debugger, stops) = Create();
        using var ownedState = state;
        using var ownedDebugger = debugger;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        state.Environment["x"] = 99;
        var closure = state.Load(
            "local _ENV={x=11}\ndo\n local _ENV={x=42}\n local y=x\n return y\nend",
            "=local-env"
        );
        debugger.SetBreakpoints("=local-env", [4]);
        var execution = state.ExecuteAsync(closure, timeout.Token).AsTask();
        var stop = await stops.Reader.ReadAsync(timeout.Token);
        var frame = (await debugger.GetStackTraceAsync(stop.Id, timeout.Token))[0].Id;
        var globals = await debugger.GetVariablesAsync(
            stop.Id,
            frame,
            LuaDebugScope.Globals,
            timeout.Token
        );
        Assert.That(globals.Single(value => value.Name == "x").Value, Is.EqualTo(new LuaValue(42)));
        Assert.That(
            (await debugger.EvaluateAsync(stop.Id, frame, "x", timeout.Token)).Value,
            Is.EqualTo(new LuaValue(42))
        );
        await debugger.SetVariableAsync(
            stop.Id,
            frame,
            LuaDebugScope.Globals,
            "x",
            "x+58",
            timeout.Token
        );
        debugger.Continue(stop.Id);
        Assert.That(await execution, Is.EqualTo(new LuaValue[] { 100 }));
        Assert.That(state.Environment["x"], Is.EqualTo(new LuaValue(99)));
    }

    [Test]
    public async Task EvaluationPreservesNilShadowingAndTheFramesGlobalMetatable()
    {
        var (state, debugger, stops) = Create();
        using var ownedState = state;
        using var ownedDebugger = debugger;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var closure = state.Load(
            "setmetatable(_G,{__index=function() return 99 end})\nx=41; y=42\nlocal y=nil\nlocal function run()\n local x=nil\n return x,y\nend\nreturn run()",
            "@nil-bindings.lua"
        );
        debugger.SetBreakpoints("nil-bindings.lua", [6]);
        var execution = state.ExecuteAsync(closure, timeout.Token).AsTask();
        var stop = await stops.Reader.ReadAsync(timeout.Token);
        var frame = (await debugger.GetStackTraceAsync(stop.Id, timeout.Token))[0].Id;
        Assert.That(
            (
                await debugger.EvaluateAsync(
                    stop.Id,
                    frame,
                    "x==nil and y==nil and missing==99",
                    timeout.Token
                )
            ).Value,
            Is.EqualTo(new LuaValue(true))
        );
        await debugger.SetVariableAsync(
            stop.Id,
            frame,
            LuaDebugScope.UpValues,
            "y",
            "41",
            timeout.Token
        );
        debugger.Continue(stop.Id);
        Assert.That(await execution, Is.EqualTo(new LuaValue[] { LuaValue.Nil, 41 }));
    }

    [Test]
    public async Task RemovingALuaHookDuringAStopDoesNotReplayIt()
    {
        var (state, debugger, stops) = Create();
        using var ownedState = state;
        using var ownedDebugger = debugger;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var hooks = 0;
        state.SetHook(
            new LuaFunction(
                (context, _) =>
                {
                    hooks++;
                    return new(context.Return());
                }
            ),
            "l",
            1
        );
        debugger.Pause();
        var execution = state.DoStringAsync("return 42", cancellationToken: timeout.Token).AsTask();
        var stop = await stops.Reader.ReadAsync(timeout.Token);
        state.SetHook(null, "");
        var stoppedHooks = hooks;
        debugger.Continue(stop.Id);
        Assert.That(await execution, Is.EqualTo(new LuaValue[] { 42 }));
        Assert.That(hooks, Is.EqualTo(stoppedHooks));
    }

    [TestCase(LuaDebugPauseMode.Asynchronous)]
    [TestCase(LuaDebugPauseMode.Synchronous)]
    public async Task StepOverMetamethodAndStepOutOfYieldingCoroutineReturnToTheCaller(
        LuaDebugPauseMode mode
    )
    {
        var (state, debugger, stops) = Create(mode);
        using var ownedState = state;
        using var ownedDebugger = debugger;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var closure = state.Load(
            "local t=setmetatable({}, {__add=function()\n return 42\nend})\nlocal x=t+t\nlocal co=coroutine.create(function()\n coroutine.yield(x)\n return x+1\nend)\nlocal ok,v=coroutine.resume(co)\nreturn ok,v",
            "@metamethod-coroutine.lua"
        );
        debugger.SetBreakpoints("metamethod-coroutine.lua", [4, 6]);
        var execution = Task.Run(() => state.ExecuteAsync(closure, timeout.Token).AsTask());
        var stop = await stops.Reader.ReadAsync(timeout.Token);
        Assert.That(stop.Line, Is.EqualTo(4));
        debugger.Continue(stop.Id, LuaDebugStep.Over);
        stop = await stops.Reader.ReadAsync(timeout.Token);
        Assert.That(stop.Line, Is.EqualTo(5));
        debugger.Continue(stop.Id);
        stop = await stops.Reader.ReadAsync(timeout.Token);
        Assert.That(stop.Line, Is.EqualTo(6));
        debugger.Continue(stop.Id, LuaDebugStep.Out);
        stop = await stops.Reader.ReadAsync(timeout.Token);
        Assert.That(stop.Line, Is.EqualTo(10));
        debugger.Continue(stop.Id);
        Assert.That(await execution, Is.EqualTo(new LuaValue[] { true, 42 }));
    }

    [Test]
    public async Task DebuggerExecutionCopiesDoNotChangeSharedPrototypesOrDumps()
    {
        using var first = LuaState.Create();
        using var second = LuaState.Create();
        var closure = first.Load("local x=40\nreturn x+2", "@shared.lua");
        var original = closure.Proto.ToBytecode();
        using var debugger = new LuaDebugger(first);
        var stops = Channel.CreateUnbounded<LuaDebugStop>();
        debugger.Stopped += stop => stops.Writer.TryWrite(stop);
        debugger.Attach();
        debugger.SetBreakpoints("shared.lua", [2]);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var execution = first.ExecuteAsync(closure, timeout.Token).AsTask();
        try
        {
            var stop = await stops.Reader.ReadAsync(timeout.Token);
            Assert.That(closure.Proto.ToBytecode(), Is.EqualTo(original));
            var shared = new Lua.Runtime.LuaClosure(second, closure.Proto);
            Assert.That(
                await second.ExecuteAsync(shared, timeout.Token),
                Is.EqualTo(new LuaValue[] { 42 })
            );
            debugger.Continue(stop.Id);
            Assert.That(await execution, Is.EqualTo(new LuaValue[] { 42 }));
            Assert.That(closure.Proto.ToBytecode(), Is.EqualTo(original));
        }
        finally
        {
            debugger.Detach();
            await execution;
        }
    }

    [Test]
    public async Task BreakpointsCanBeInstalledAndClearedDuringExecution()
    {
        var (state, debugger, stops) = Create();
        using var ownedState = state;
        using var ownedDebugger = debugger;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var running = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        state.Environment["tick"] = new LuaFunction(
            (context, _) =>
            {
                running.TrySetResult(true);
                return new(context.Return());
            }
        );
        var closure = state.Load(
            "local n=0\nwhile not done do\n n=n+1; tick()\nend\nreturn n",
            "@live.lua"
        );
        var execution = Task.Run(async () => await state.ExecuteAsync(closure, timeout.Token));
        await running.Task.WaitAsync(timeout.Token);
        debugger.SetBreakpoints("live.lua", [3]);
        var stop = await stops.Reader.ReadAsync(timeout.Token);
        Assert.That(stop.Reason, Is.EqualTo("breakpoint"));
        await debugger.SetVariableAsync(
            stop.Id,
            1,
            LuaDebugScope.Globals,
            "done",
            "true",
            timeout.Token
        );
        debugger.SetBreakpoints("live.lua", []);
        debugger.Continue(stop.Id);
        Assert.That((await execution)[0].Read<double>(), Is.GreaterThan(0));
    }

    [Test]
    public async Task FailedEvaluationDoesNotInvokeTheProgramsErrorHandler()
    {
        var (state, debugger, stops) = Create();
        using var ownedState = state;
        using var ownedDebugger = debugger;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var closure = state.Load(
            "local n=0\nlocal function run()\n local x=42\n return x\nend\nlocal ok,v=xpcall(run,function(e) n=n+1;return e end)\nreturn ok,v,n",
            "@evaluation-error.lua"
        );
        debugger.RegisterSource(closure);
        debugger.SetBreakpoints("evaluation-error.lua", [4]);
        var execution = state.ExecuteAsync(closure, timeout.Token).AsTask();
        var stop = await stops.Reader.ReadAsync(timeout.Token);
        var frame = (await debugger.GetStackTraceAsync(stop.Id, timeout.Token))[0].Id;
        try
        {
            await debugger.EvaluateAsync(stop.Id, frame, "error('watch failed')", timeout.Token);
            Assert.Fail();
        }
        catch (LuaRuntimeException) { }
        Assert.That(
            (await debugger.EvaluateAsync(stop.Id, frame, "x+1", timeout.Token)).Value,
            Is.EqualTo(new LuaValue(43))
        );
        debugger.Continue(stop.Id);
        Assert.That(await execution, Is.EqualTo(new LuaValue[] { true, 42, 0 }));
    }

    [TestCase(LuaDebugPauseMode.Asynchronous)]
    [TestCase(LuaDebugPauseMode.Synchronous)]
    public async Task DetachCancelsAnInspectionAndReleasesTheStoppedProgram(LuaDebugPauseMode mode)
    {
        var (state, debugger, stops) = Create(mode);
        using var ownedState = state;
        using var ownedDebugger = debugger;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var started = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        var waiting = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        state.Environment["wait"] = new LuaFunction(
            async (context, token) =>
            {
                started.TrySetResult(true);
                await waiting.Task.WaitAsync(token);
                return context.Return();
            }
        );
        debugger.Pause();
        var execution = Task.Run(async () =>
            await state.DoStringAsync("return 42", cancellationToken: timeout.Token)
        );
        var stop = await stops.Reader.ReadAsync(timeout.Token);
        var evaluation = debugger.EvaluateAsync(stop.Id, 1, "wait()", timeout.Token);
        await started.Task.WaitAsync(timeout.Token);
        debugger.Detach();
        try
        {
            await evaluation;
            Assert.Fail();
        }
        catch (OperationCanceledException) { }
        Assert.That(await execution, Is.EqualTo(new LuaValue[] { 42 }));
    }

    [TestCase(0, LuaDebugPauseMode.Asynchronous)]
    [TestCase(1, LuaDebugPauseMode.Asynchronous)]
    [TestCase(0, LuaDebugPauseMode.Synchronous)]
    [TestCase(1, LuaDebugPauseMode.Synchronous)]
    public async Task StoppingDoesNotDuplicateLuaHookEvents(int interval, LuaDebugPauseMode mode)
    {
        static async Task<string[]> Run(bool debug, int interval, LuaDebugPauseMode mode)
        {
            using var state = LuaState.Create();
            var events = new List<string>();
            state.SetHook(
                new LuaFunction(
                    (context, _) =>
                    {
                        events.Add(
                            context.GetArgument<string>(0) + ":" + context.GetArgument(1).ToString()
                        );
                        return new(context.Return());
                    }
                ),
                "l",
                interval
            );
            using var debugger = new LuaDebugger(
                state,
                new LuaDebuggerOptions { PauseMode = mode }
            );
            var stops = Channel.CreateUnbounded<LuaDebugStop>();
            debugger.Stopped += stop => stops.Writer.TryWrite(stop);
            if (debug)
            {
                debugger.Attach();
                debugger.Pause();
            }
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var execution = Task.Run(() =>
                state
                    .DoStringAsync(
                        "local x=0\nfor i=1,3 do\n x=x+i\nend\nreturn x",
                        "@hooks.lua",
                        timeout.Token
                    )
                    .AsTask()
            );
            if (debug)
                debugger.Continue((await stops.Reader.ReadAsync(timeout.Token)).Id);
            await execution;
            return events.ToArray();
        }
        Assert.That(await Run(true, interval, mode), Is.EqualTo(await Run(false, interval, mode)));
    }

    static (LuaState, LuaDebugger, Channel<LuaDebugStop>) Create(
        LuaDebugPauseMode mode = LuaDebugPauseMode.Asynchronous
    )
    {
        var state = LuaState.Create();
        state.OpenStandardLibraries();
        var debugger = new LuaDebugger(state, new LuaDebuggerOptions { PauseMode = mode });
        var stops = Channel.CreateUnbounded<LuaDebugStop>();
        debugger.Stopped += stop => stops.Writer.TryWrite(stop);
        debugger.Attach();
        return (state, debugger, stops);
    }

    [Test]
    public async Task BreakpointInspectionAndEditingRunWhileExecutionIsSuspended()
    {
        var (state, debugger, stops) = Create();
        using var ownedState = state;
        using var ownedDebugger = debugger;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var closure = state.Load(
            "local x=1\nlocal t={a=7}\nx=x+2\nreturn x,t.a",
            "@inspection.lua"
        );
        var pending = debugger.SetBreakpoints("inspection.lua", [3]);
        Assert.That(pending[0].Verified, Is.False);
        debugger.RegisterSource(closure);
        var execution = state.ExecuteAsync(closure, timeout.Token).AsTask();
        var stop = await stops.Reader.ReadAsync(timeout.Token);
        Assert.That(stop.Line, Is.EqualTo(3));
        Assert.That(execution.IsCompleted, Is.False);
        var frames = await debugger.GetStackTraceAsync(stop.Id, timeout.Token);
        var frameId = frames[0].Id;
        var locals = await debugger.GetVariablesAsync(
            stop.Id,
            frameId,
            LuaDebugScope.Locals,
            timeout.Token
        );
        Assert.That(locals.Single(x => x.Name == "x").Value, Is.EqualTo(new LuaValue(1)));
        var table = locals.Single(x => x.Name == "t").Value;
        Assert.That(
            (await debugger.GetChildrenAsync(stop.Id, table, cancellationToken: timeout.Token))[
                0
            ].Value,
            Is.EqualTo(new LuaValue(7))
        );
        Assert.That(
            (await debugger.EvaluateAsync(stop.Id, frameId, "x+t.a", timeout.Token)).Value,
            Is.EqualTo(new LuaValue(8))
        );
        await debugger.SetVariableAsync(
            stop.Id,
            frameId,
            LuaDebugScope.Locals,
            "x",
            "10",
            timeout.Token
        );
        debugger.Continue(stop.Id);
        Assert.That(await execution, Is.EqualTo(new LuaValue[] { 12, 7 }));
        Assert.That(debugger.CurrentStop, Is.Null);
        Assert.Throws<InvalidOperationException>(() => debugger.Continue(stop.Id));
    }

    [Test]
    public async Task StepIntoOverAndOutFollowSourceLines()
    {
        var (state, debugger, stops) = Create();
        using var ownedState = state;
        using var ownedDebugger = debugger;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var closure = state.Load(
            "local function leaf()\n local x=1\n return x+1\nend\nlocal v=leaf()\nv=v+1\nreturn v",
            "@steps.lua"
        );
        debugger.RegisterSource(closure);
        debugger.SetBreakpoints("steps.lua", [5]);
        var execution = state.ExecuteAsync(closure, timeout.Token).AsTask();
        var stop = await stops.Reader.ReadAsync(timeout.Token);
        Assert.That(stop.Line, Is.EqualTo(5));
        debugger.Continue(stop.Id, LuaDebugStep.Into);
        stop = await stops.Reader.ReadAsync(timeout.Token);
        Assert.That(stop.Line, Is.EqualTo(2));
        debugger.Continue(stop.Id, LuaDebugStep.Over);
        stop = await stops.Reader.ReadAsync(timeout.Token);
        Assert.That(stop.Line, Is.EqualTo(3));
        debugger.Continue(stop.Id, LuaDebugStep.Out);
        stop = await stops.Reader.ReadAsync(timeout.Token);
        Assert.That(stop.Line, Is.EqualTo(6));
        debugger.Continue(stop.Id);
        Assert.That(await execution, Is.EqualTo(new LuaValue[] { 3 }));
    }

    [Test]
    public async Task StepOverTailCallWaitsForTheOriginalFrameToReturn()
    {
        var (state, debugger, stops) = Create();
        using var ownedState = state;
        using var ownedDebugger = debugger;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var closure = state.Load(
            "local function leaf()\n return 42\nend\nlocal function tail()\n return leaf()\nend\nlocal x=tail()\nreturn x",
            "@tail.lua"
        );
        debugger.RegisterSource(closure);
        debugger.SetBreakpoints("tail.lua", [5]);
        var execution = state.ExecuteAsync(closure, timeout.Token).AsTask();
        var stop = await stops.Reader.ReadAsync(timeout.Token);
        Assert.That(stop.Line, Is.EqualTo(5));
        debugger.Continue(stop.Id, LuaDebugStep.Over);
        stop = await stops.Reader.ReadAsync(timeout.Token);
        Assert.That(stop.Line, Is.EqualTo(8));
        debugger.Continue(stop.Id);
        Assert.That(await execution, Is.EqualTo(new LuaValue[] { 42 }));
    }

    [TestCase("ready(); while true do end")]
    [TestCase("ready(); for i=1,1e15 do end")]
    [TestCase("ready(); for i=1,1e15 do local n=i+i end")]
    public async Task PauseInterruptsASingleLineLoopAndCancellationReleasesTheStop(string source)
    {
        var (state, debugger, stops) = Create();
        using var ownedState = state;
        using var ownedDebugger = debugger;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var cancellation = new CancellationTokenSource();
        var started = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        state.Environment["ready"] = new LuaFunction(
            (context, _) =>
            {
                started.TrySetResult(true);
                return new(context.Return());
            }
        );
        var execution = Task.Run(async () =>
            await state.DoStringAsync(source, cancellationToken: cancellation.Token)
        );
        await started.Task.WaitAsync(timeout.Token);
        debugger.Pause();
        var stop = await stops.Reader.ReadAsync(timeout.Token);
        Assert.That(stop.Reason, Is.EqualTo("pause"));
        cancellation.Cancel();
        try
        {
            await execution.WaitAsync(timeout.Token);
            Assert.Fail("Execution should be cancelled.");
        }
        catch (LuaCanceledException) { }
        Assert.That(debugger.CurrentStop, Is.Null);
        debugger.Detach();
        Assert.That(await state.DoStringAsync("return 42"), Is.EqualTo(new LuaValue[] { 42 }));
    }

    [Test]
    public async Task DebuggerDoesNotReplaceLuaHooksAndCoversExistingCoroutines()
    {
        using var state = LuaState.Create();
        state.OpenStandardLibraries();
        await state.DoStringAsync(
            "hooks=0; co=coroutine.create(function()\n local x=40\n return x+2\nend); debug.sethook(function() hooks=hooks+1 end,'l')",
            "@coroutine.lua"
        );
        using var debugger = new LuaDebugger(state);
        var stops = Channel.CreateUnbounded<LuaDebugStop>();
        debugger.Stopped += stop => stops.Writer.TryWrite(stop);
        debugger.Attach();
        debugger.SetBreakpoints("coroutine.lua", [3]);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var execution = state
            .DoStringAsync("return coroutine.resume(co)", cancellationToken: timeout.Token)
            .AsTask();
        var stop = await stops.Reader.ReadAsync(timeout.Token);
        Assert.That(stop.Line, Is.EqualTo(3));
        debugger.Detach();
        Assert.That(await execution, Is.EqualTo(new LuaValue[] { true, 42 }));
        Assert.That(state.Environment["hooks"].Read<double>(), Is.GreaterThan(0));
    }

    [TestCase(LuaDebugPauseMode.Asynchronous)]
    [TestCase(LuaDebugPauseMode.Synchronous)]
    public async Task InspectingInsideTheStoppedHandlerFailsInsteadOfHanging(LuaDebugPauseMode mode)
    {
        using var state = LuaState.Create();
        using var debugger = new LuaDebugger(state, new LuaDebuggerOptions { PauseMode = mode });
        var closure = state.Load("local x = 1\nreturn x", "@stopped-handler.lua");
        debugger.Attach();
        debugger.RegisterSource(closure);
        debugger.SetBreakpoints("stopped-handler.lua", [1]);
        InvalidOperationException? failure = null;
        var stops = 0;
        debugger.Stopped += stop =>
        {
            stops++;
            failure = Assert.Throws<InvalidOperationException>(() =>
            {
                _ = debugger.GetVariablesAsync(stop.Id, 1, LuaDebugScope.Locals);
            });
            debugger.Continue(stop.Id);
        };
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        Assert.That(
            await state.ExecuteAsync(closure, timeout.Token),
            Is.EqualTo(new LuaValue[] { 1 })
        );
        Assert.That(stops, Is.EqualTo(1));
        Assert.That(failure, Is.Not.Null);
        Assert.That(failure!.Message, Does.Contain("after the Stopped handler returns"));
    }

    [Test]
    public async Task PauseRequestDoesNotOutliveItsExecution()
    {
        using var state = LuaState.Create();
        using var debugger = new LuaDebugger(state);
        var closure = state.Load("wait()\nreturn 1", "@pause-scope.lua");
        debugger.Attach();
        debugger.RegisterSource(closure);
        var entered = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        state.Environment["wait"] = new LuaFunction(
            async (context, token) =>
            {
                entered.TrySetResult(true);
                await Task.Delay(Timeout.Infinite, token);
                return context.Return();
            }
        );
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var execution = state.ExecuteAsync(closure, cancellation.Token).AsTask();
        await entered.Task.WaitAsync(cancellation.Token);
        // The pause arrives while the execution is alive but outside any Lua checkpoint.
        debugger.Pause();
        cancellation.Cancel();
        try
        {
            await execution;
            Assert.Fail("Execution should be cancelled.");
        }
        catch (LuaCanceledException) { }

        var stops = 0;
        debugger.Stopped += stop =>
        {
            stops++;
            debugger.Continue(stop.Id);
        };
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        Assert.That(
            await state.ExecuteAsync(state.Load("return 2", "@pause-scope-2.lua"), timeout.Token),
            Is.EqualTo(new LuaValue[] { 2 })
        );
        Assert.That(stops, Is.Zero);
    }
}
