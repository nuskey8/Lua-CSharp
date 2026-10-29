using Lua.Platforms;
using Lua.Runtime;
using Lua.Standard;

namespace Lua.Tests;

[TestFixture(LuaStringMode.Utf16)]
[TestFixture(LuaStringMode.Bytes)]
public class CoroutineCancellationDispatchTests(LuaStringMode mode)
{
    LuaState CreateState()
    {
        var state = LuaState.Create(
            mode == LuaStringMode.Bytes ? LuaPlatform.DefaultByteStrings : LuaPlatform.Default
        );
        state.OpenStandardLibraries();
        return state;
    }

    [Test]
    public async Task LaterResumeCancellationReachesCallbacks(
        [Values(
            "call",
            "add",
            "unm",
            "len",
            "concat",
            "lt",
            "le",
            "eq",
            "index",
            "newindex",
            "callable",
            "iterator",
            "hook"
        )]
            string operation,
        [Values] bool asynchronous
    )
    {
        using var state = CreateState();
        using var cancellation = new CancellationTokenSource();
        var calls = 0;
        var work = new LuaFunction(
            async (context, token) =>
            {
                calls++;
                if (asynchronous)
                    await Task.Yield();
                cancellation.Cancel();
                token.ThrowIfCancellationRequested();
                return context.Return(42);
            }
        );
        state.Environment["work"] = work;
        var expression = operation switch
        {
            "call" or "hook" => "return work()",
            "add" => "return a+a",
            "unm" => "return -a",
            "len" => "return #a",
            "concat" => "return a..a",
            "lt" => "return a<b",
            "le" => "return a<=b",
            "eq" => "return a==b",
            "index" => "return a.missing",
            "newindex" => "a.missing=1",
            "callable" => "return a()",
            "iterator" => "for value in work do return value end",
            _ => throw new ArgumentOutOfRangeException(nameof(operation)),
        };
        using var thread = state.CreateCoroutine(
            state.Load(
                """
                local mt={__add=work,__unm=work,__len=work,__concat=work,__lt=work,
                    __le=work,__eq=work,__index=work,__newindex=work,__call=work}
                local a,b=setmetatable({},mt),setmetatable({},mt)
                coroutine.yield(7)
                """ + expression,
                "later-resume-dispatch"
            )
        );
        var output = new LuaStack();
        await thread.ResumeAsync(output);
        Assert.That(output.AsSpan().ToArray(), Is.EqualTo(new LuaValue[] { true, 7 }));
        if (operation == "hook")
            thread.SetHook(work, "l");
        output.Clear();
        var error = Assert.CatchAsync<OperationCanceledException>(async () =>
            await thread.ResumeAsync(output, cancellation.Token)
        )!;
        Assert.That(error.CancellationToken, Is.EqualTo(cancellation.Token));
        Assert.That(calls, Is.EqualTo(1));
        Assert.That(thread.GetStatus(), Is.EqualTo(LuaThreadStatus.Dead));
        Assert.That(thread.CoroutineFunction, Is.Null);
        Assert.That(thread.CallStackFrameCount, Is.Zero);
        Assert.That(thread.GetStackValues().Length, Is.Zero);
    }

    [Test]
    public async Task LaterResumeCancellationIsCheckedByTheVm([Values] bool protectedMode)
    {
        using var state = CreateState();
        using var cancellation = new CancellationTokenSource();
        state.Environment["cancel"] = new LuaFunction(
            (context, _) =>
            {
                cancellation.Cancel();
                return new(context.Return());
            }
        );
        using var thread = state.CreateCoroutine(
            state.Load(
                "coroutine.yield(); cancel(); local n=0; for i=1,1000 do n=n+i end; return n",
                "later-resume-loop"
            ),
            protectedMode
        );
        var output = new LuaStack();
        await thread.ResumeAsync(output);
        output.Clear();
        var error = Assert.CatchAsync<OperationCanceledException>(async () =>
            await thread.ResumeAsync(output, cancellation.Token)
        )!;
        Assert.That(error.CancellationToken, Is.EqualTo(cancellation.Token));
        Assert.That(thread.GetStatus(), Is.EqualTo(LuaThreadStatus.Dead));
    }

    [Test]
    public async Task LaterResumeCancellationStopsAnAwaitingCallback(
        [Values] bool protectedMode,
        [Values("lua", "callback", "entry")] string path
    )
    {
        using var state = CreateState();
        using var cancellation = new CancellationTokenSource();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken received = default;
        var work = new LuaFunction(
            async (context, token) =>
            {
                received = token;
                if (path != "lua")
                    await context.State.YieldAsync(context, token);
                entered.SetResult();
                await Task.Delay(500, token);
                return context.Return(42);
            }
        );
        state.Environment["work"] = work;
        using var thread = state.CreateCoroutine(
            path == "entry"
                ? work
                : state.Load(
                    path == "lua" ? "coroutine.yield(); return work()" : "return work()",
                    "cancel-awaiting-callback"
                ),
            protectedMode
        );
        var output = new LuaStack();
        await thread.ResumeAsync(output);
        output.Clear();
        var running = thread.ResumeAsync(output, cancellation.Token).AsTask();
        await entered.Task;
        cancellation.Cancel();
        var error = Assert.CatchAsync<OperationCanceledException>(async () => await running)!;
        Assert.That(error.CancellationToken, Is.EqualTo(cancellation.Token));
        Assert.That(received.IsCancellationRequested, Is.True);
        Assert.That(thread.GetStatus(), Is.EqualTo(LuaThreadStatus.Dead));
        Assert.That(thread.CallStackFrameCount, Is.Zero);
    }

    [Test]
    public async Task LaterCancellationReachesNestedAndRestoredVmExecutions(
        [Values] bool afterNestedReturn
    )
    {
        using var state = CreateState();
        using var cancellation = new CancellationTokenSource();
        var inner = state.Load(
            "coroutine.yield(7); local n=0; for i=1,1000 do n=n+i end; return n",
            "nested-execution"
        );
        state.Environment["nested"] = new LuaFunction(
            async (context, token) =>
            {
                await context.State.RunAsync(inner, 0, token);
                return context.Return(42);
            }
        );
        using var thread = state.CreateCoroutine(
            state.Load(
                "nested(); coroutine.yield(8); local n=0; for i=1,1000 do n=n+i end; return n",
                "outer-execution"
            )
        );
        var output = new LuaStack();
        await thread.ResumeAsync(output);
        Assert.That(output.AsSpan().ToArray(), Is.EqualTo(new LuaValue[] { true, 7 }));
        output.Clear();
        if (afterNestedReturn)
        {
            await thread.ResumeAsync(output);
            Assert.That(output.AsSpan().ToArray(), Is.EqualTo(new LuaValue[] { true, 8 }));
            output.Clear();
        }
        thread.SetHook(
            new LuaFunction(
                (context, _) =>
                {
                    cancellation.Cancel();
                    return new(context.Return());
                }
            ),
            "",
            1
        );
        var error = Assert.CatchAsync<OperationCanceledException>(async () =>
            await thread.ResumeAsync(output, cancellation.Token)
        )!;
        Assert.That(error.CancellationToken, Is.EqualTo(cancellation.Token));
        Assert.That(thread.GetStatus(), Is.EqualTo(LuaThreadStatus.Dead));
        Assert.That(thread.CallStackFrameCount, Is.Zero);
    }

    [Test]
    public async Task ReplacedResumeTokenDoesNotCancelTheNextResume([Values] bool firstCancellable)
    {
        using var state = CreateState();
        using var lifetime = new CancellationTokenSource();
        using var previous = new CancellationTokenSource();
        state.Environment["cancelPrevious"] = new LuaFunction(
            (context, token) =>
            {
                previous.Cancel();
                token.ThrowIfCancellationRequested();
                return new(context.Return(42));
            }
        );
        using var thread = state.CreateCoroutine(
            state.Load(
                "coroutine.yield(1); coroutine.yield(2); coroutine.yield(3); return cancelPrevious()",
                "replace-resume-token"
            )
        );
        var output = new LuaStack();
        await thread.ResumeAsync(output, firstCancellable ? lifetime.Token : default);
        output.Clear();
        await thread.ResumeAsync(output, previous.Token);
        Assert.That(output.AsSpan().ToArray(), Is.EqualTo(new LuaValue[] { true, 2 }));
        output.Clear();
        await thread.ResumeAsync(output, previous.Token);
        Assert.That(output.AsSpan().ToArray(), Is.EqualTo(new LuaValue[] { true, 3 }));
        output.Clear();
        await thread.ResumeAsync(output);
        Assert.That(output.AsSpan().ToArray(), Is.EqualTo(new LuaValue[] { true, 42 }));
        Assert.That(thread.GetStatus(), Is.EqualTo(LuaThreadStatus.Dead));
    }
}
