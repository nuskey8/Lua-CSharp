using System.Runtime.CompilerServices;
using Lua.Platforms;
using Lua.Runtime;
using Lua.Standard;

namespace Lua.Tests;

[TestFixture(LuaStringMode.Utf16)]
[TestFixture(LuaStringMode.Bytes)]
public class CoroutineCaptureLifetimeTests(LuaStringMode mode)
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
    [Combinatorial]
    public async Task AbandonedCoroutineDoesNotKeepUnrelatedLocalsAlive(
        [Values(false, true)] bool wrap,
        [Values(false, true)] bool selfReference,
        [Values(false, true)] bool yieldClosure
    )
    {
        using var state = CreateState();
        var payload = InstallPayload(state);
        WeakReference? thread = null;
        state.Environment["observe"] = new LuaFunction(
            (context, _) =>
            {
                thread = new WeakReference(context.State);
                return new(context.Return());
            }
        );
        state.Environment["wrap"] = wrap;
        state.Environment["selfReference"] = selfReference;
        state.Environment["yieldClosure"] = yieldClosure;
        var closure = (
            await state.DoStringAsync(
                """
                local function body()
                    observe()
                    local keep=42
                    local junk=payload
                    payload=nil
                    local self=selfReference and coroutine.running()
                    local capture=function() return keep end
                    if yieldClosure then
                        coroutine.yield(capture)
                    else
                        saved=capture
                        coroutine.yield()
                    end
                    return junk,self
                end
                local run
                if wrap then
                    run=coroutine.wrap(body)
                else
                    local co=coroutine.create(body)
                    run=function()
                        local ok,value=coroutine.resume(co)
                        assert(ok)
                        return value
                    end
                end
                local yielded=run()
                return yieldClosure and yielded or saved
                """
            )
        )[0].Read<LuaClosure>();
        Collect();
        Assert.That(thread, Is.Not.Null);
        Assert.That(thread!.IsAlive, Is.False);
        Assert.That(payload.IsAlive, Is.False);
        Assert.That(closure.UpValues[0].IsClosed, Is.True);
        Assert.That(closure.UpValues[0].Thread, Is.Null);
        Assert.That(await state.ExecuteAsync(closure), Is.EqualTo(new LuaValue[] { 42 }));
        closure.UpValues[0].SetValue(43);
        Assert.That(await state.ExecuteAsync(closure), Is.EqualTo(new LuaValue[] { 43 }));
        GC.KeepAlive(closure);
        GC.KeepAlive(state);
    }

    [Test]
    public async Task SuspendedCapturesShareUpdatesWithDebugLocalsAndResumedRegisters()
    {
        using var state = CreateState();
        var result = await state.DoStringAsync(
            """
            local co=coroutine.create(function()
                local keep=1
                get=function() return keep end
                set=function(value) keep=value end
                coroutine.yield()
                keep=keep+10
                coroutine.yield(keep)
                return keep
            end)
            assert(coroutine.resume(co))
            local identity=debug.upvalueid(get,1)
            set(2)
            local name,before=debug.getlocal(co,1,1)
            assert(debug.setlocal(co,1,1,3)==name)
            local fromClosure=get()
            local ok,yielded=coroutine.resume(co)
            assert(ok)
            set(20)
            local again,done=coroutine.resume(co)
            assert(again)
            return before,fromClosure,yielded,done,get(),identity==debug.upvalueid(get,1)
            """
        );
        Assert.That(result, Is.EqualTo(new LuaValue[] { 2, 3, 13, 20, 20, true }));
    }

    [Test]
    public async Task UpdatingSuspendedCaptureReleasesItsPreviousValue()
    {
        using var state = CreateState();
        var payload = InstallPayload(state);
        await state.DoStringAsync(
            """
            co=coroutine.create(function()
                local keep=payload
                payload=nil
                get=function() return keep end
                set=function(value) keep=value end
                coroutine.yield()
                return keep
            end)
            assert(coroutine.resume(co))
            set(42)
            """
        );
        Collect();
        Assert.That(payload.IsAlive, Is.False);
        Assert.That(
            await state.DoStringAsync("return coroutine.resume(co)"),
            Is.EqualTo(new LuaValue[] { true, 42 })
        );
        GC.KeepAlive(state);
    }

    [Test]
    public async Task ExplicitlyClosedSuspendedCaptureDoesNotReplaceItsFormerLocal()
    {
        using var state = CreateState();
        var closure = (
            await state.DoStringAsync(
                """
                co=coroutine.create(function()
                    local keep=1
                    get=function() return keep end
                    coroutine.yield()
                    keep=keep+10
                    newer=function() return keep end
                    return keep
                end)
                assert(coroutine.resume(co))
                return get
                """
            )
        )[0].Read<LuaClosure>();
        closure.UpValues[0].Close();
        closure.UpValues[0].SetValue(999);
        var result = await state.DoStringAsync(
            "local name,value=debug.getlocal(co,1,1);local ok,done=coroutine.resume(co);return value,ok,done,get(),newer()"
        );
        Assert.That(result, Is.EqualTo(new LuaValue[] { 1, true, 11, 999, 11 }));
    }

    [Test]
    public async Task ResumedCapturesSurviveStackGrowth()
    {
        using var state = CreateState();
        var thread = (
            await state.DoStringAsync(
                """
                local co=coroutine.create(function()
                    local keep=1
                    get=function() return keep end
                    set=function(value) keep=value end
                    local values={coroutine.yield()}
                    keep=keep+values[300]
                    return keep
                end)
                assert(coroutine.resume(co))
                set(3)
                return co
                """
            )
        )[0].Read<LuaState>();
        var arguments = new LuaStack();
        for (var i = 1; i <= 400; i++)
            arguments.Push(i);
        await thread.ResumeAsync(arguments);
        Assert.That(arguments.AsSpan().ToArray(), Is.EqualTo(new LuaValue[] { true, 303 }));
        Assert.That(await state.DoStringAsync("return get()"), Is.EqualTo(new LuaValue[] { 303 }));
    }

    [Test]
    public async Task CancellationClosesTheLatestSuspendedValue()
    {
        using var state = CreateState();
        using var cancellation = new CancellationTokenSource();
        var closure = (
            await state.DoStringAsync(
                """
                co=coroutine.create(function()
                    local keep=1
                    get=function() return keep end
                    set=function(value) keep=value end
                    coroutine.yield()
                    return keep
                end)
                assert(coroutine.resume(co))
                return get
                """,
                cancellationToken: cancellation.Token
            )
        )[0].Read<LuaClosure>();
        await state.DoStringAsync("set(7)");
        cancellation.Cancel();
        var thread = state.Environment["co"].Read<LuaState>();
        Assert.That(() => thread.GetStatus(), Is.EqualTo(LuaThreadStatus.Dead).After(1000, 10));
        Assert.That(closure.UpValues[0].IsClosed, Is.True);
        Assert.That(await state.ExecuteAsync(closure), Is.EqualTo(new LuaValue[] { 7 }));
        Assert.That(thread.GetStackValues().Length, Is.Zero);
    }

    static void Collect()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static WeakReference InstallPayload(LuaState state)
    {
        var value = new object();
        state.Environment["payload"] = LuaValue.FromObject(value);
        return new WeakReference(value);
    }
}
