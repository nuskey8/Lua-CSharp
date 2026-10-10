using Lua.Runtime;
using Lua.Standard;

namespace Lua.Tests;

public class CoroutineHookTests
{
    const string Loop = "local x=0 for i=1,100 do x=x+i end return x";

    [Test]
    [Combinatorial]
    public async Task HostHook_NewChild_InheritsAllEvents(
        [Values("thread", "coroutine", "create", "wrap")] string creation,
        [Values(false, true)] bool asynchronous
    )
    {
        using var parent = LuaState.Create();
        parent.OpenCoroutineLibrary();
        var events = new Dictionary<LuaState, HashSet<string>>();
        parent.Environment["echo"] = new LuaFunction((c, ct) => new(c.Return(c.GetArgument(0))));
        parent.SetHook(
            new(
                async (c, ct) =>
                {
                    if (asynchronous)
                        await Task.Yield();
                    if (c.State != parent)
                    {
                        if (!events.TryGetValue(c.State, out var childEvents))
                        {
                            events.Add(c.State, childEvents = new());
                        }
                        childEvents.Add(c.GetArgument<string>(0));
                    }
                    return c.Return();
                }
            ),
            "lcr",
            4
        );
        const string source = "local x=0\nfor i=1,100 do x=x+i end\necho(x)\nreturn x";
        LuaValue[] result;
        if (creation == "thread")
        {
            using var child = parent.CreateThread();
            result = await child.DoStringAsync(source);
        }
        else if (creation == "coroutine")
        {
            using var child = parent.CreateCoroutine(parent.Load(source, "child.lua"));
            var buffer = new LuaStack();
            await child.ResumeAsync(buffer);
            result = buffer.AsSpan().ToArray();
            Assert.That(result[0], Is.EqualTo((LuaValue)true));
        }
        else
        {
            result = await parent.DoStringAsync(
                creation == "create"
                    ? "return coroutine.resume(coroutine.create(function() " + source + " end))"
                    : "return coroutine.wrap(function() " + source + " end)()"
            );
            if (creation == "create")
                Assert.That(result[0], Is.EqualTo((LuaValue)true));
        }
        Assert.That(result[^1], Is.EqualTo((LuaValue)5050));
        Assert.That(events.Count, Is.EqualTo(1));
        Assert.That(
            events.Values.Single(),
            Is.SupersetOf(new[] { "count", "line", "call", "return" })
        );
        Assert.That(parent.CallStackFrameCount, Is.Zero);
        Assert.That(events.Keys.Single().CallStackFrameCount, Is.Zero);
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task HostHook_ParentChanges_DoNotChangeExistingChild(bool remove)
    {
        using var parent = LuaState.Create();
        var first = new Dictionary<LuaState, int>();
        var second = new Dictionary<LuaState, int>();
        parent.SetHook(Counter(first), "", 4);
        using var child = parent.CreateThread();
        parent.SetHook(remove ? null : Counter(second), "", 4);
        using var laterChild = parent.CreateThread();
        await child.DoStringAsync(Loop);
        await laterChild.DoStringAsync(Loop);
        Assert.That(first.GetValueOrDefault(child), Is.EqualTo(51));
        Assert.That(first.GetValueOrDefault(laterChild), Is.Zero);
        Assert.That(second.GetValueOrDefault(child), Is.Zero);
        Assert.That(second.GetValueOrDefault(laterChild), Is.EqualTo(remove ? 0 : 51));
    }

    [Test]
    public async Task HostHook_InstalledAfterChildCreation_DoesNotApplyRetroactively()
    {
        using var parent = LuaState.Create();
        using var existingChild = parent.CreateThread();
        var counts = new Dictionary<LuaState, int>();
        parent.SetHook(Counter(counts), "", 4);
        using var laterChild = parent.CreateThread();
        await existingChild.DoStringAsync(Loop);
        await laterChild.DoStringAsync(Loop);
        Assert.That(counts.GetValueOrDefault(existingChild), Is.Zero);
        Assert.That(counts.GetValueOrDefault(laterChild), Is.EqualTo(51));
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task HostHook_ResumedChild_KeepsItsSnapshotAfterParentChanges(bool remove)
    {
        using var parent = LuaState.Create();
        parent.OpenCoroutineLibrary();
        var original = new Dictionary<LuaState, int>();
        var replacement = new Dictionary<LuaState, int>();
        parent.SetHook(Counter(original), "", 4);
        using var child = parent.CreateCoroutine(
            parent.Load("coroutine.yield(7) " + Loop, "child.lua")
        );
        var first = new LuaStack();
        await child.ResumeAsync(first);
        Assert.That(first.AsSpan().ToArray(), Is.EqualTo(new LuaValue[] { true, 7 }));
        var firesBeforeResume = original.GetValueOrDefault(child);
        parent.SetHook(remove ? null : Counter(replacement), "", 64);
        var second = new LuaStack();
        await child.ResumeAsync(second);
        Assert.That(second.AsSpan().ToArray(), Is.EqualTo(new LuaValue[] { true, 5050 }));
        Assert.That(original.GetValueOrDefault(child), Is.GreaterThan(firesBeforeResume));
        Assert.That(replacement.GetValueOrDefault(child), Is.Zero);
    }

    [Test]
    public async Task HostHook_ChildOverrideAndRemoval_DoNotChangeParentOrSibling()
    {
        using var parent = LuaState.Create();
        var original = new Dictionary<LuaState, int>();
        var replacement = new Dictionary<LuaState, int>();
        parent.SetHook(Counter(original), "", 4);
        using var child = parent.CreateThread();
        using var sibling = parent.CreateThread();
        child.SetHook(Counter(replacement), "", 4);
        await child.DoStringAsync(Loop);
        await sibling.DoStringAsync(Loop);
        await parent.DoStringAsync(Loop);
        Assert.That(original.GetValueOrDefault(child), Is.Zero);
        Assert.That(original.GetValueOrDefault(sibling), Is.EqualTo(51));
        Assert.That(original.GetValueOrDefault(parent), Is.EqualTo(51));
        Assert.That(replacement.GetValueOrDefault(child), Is.EqualTo(51));

        child.SetHook(null, "");
        using var grandchild = child.CreateThread();
        await child.DoStringAsync(Loop);
        await grandchild.DoStringAsync(Loop);
        Assert.That(replacement.GetValueOrDefault(child), Is.EqualTo(51));
        Assert.That(original.GetValueOrDefault(grandchild), Is.Zero);
        Assert.That(replacement.GetValueOrDefault(grandchild), Is.Zero);
    }

    [TestCase("create")]
    [TestCase("wrap")]
    public async Task HostHook_NestedCoroutine_InheritsFromCreatingThread(string creation)
    {
        using var parent = LuaState.Create();
        parent.OpenCoroutineLibrary();
        var original = new Dictionary<LuaState, int>();
        var replacement = new Dictionary<LuaState, int>();
        parent.SetHook(Counter(original), "", 4);
        LuaState? creator = null;
        parent.Environment["install"] = new LuaFunction(
            (c, ct) =>
            {
                creator = c.State;
                c.State.SetHook(Counter(replacement), "", 4);
                return new(c.Return());
            }
        );
        var inner =
            creation == "create"
                ? "local ok,x=coroutine.resume(coroutine.create(function() "
                    + Loop
                    + " end)) return x"
                : "return coroutine.wrap(function() " + Loop + " end)()";
        var result = await parent.DoStringAsync(
            "return coroutine.resume(coroutine.create(function() install() " + inner + " end))"
        );
        Assert.That(result, Is.EqualTo(new LuaValue[] { true, 5050 }));
        Assert.That(creator, Is.Not.Null);
        Assert.That(replacement.Keys.Any(x => x != creator), Is.True);
        var grandchild = replacement.Keys.Single(x => x != creator);
        Assert.That(replacement[grandchild], Is.EqualTo(51));
        Assert.That(original.GetValueOrDefault(grandchild), Is.Zero);
    }

    [Test]
    public async Task HostHook_ChildCounter_StartsWithFullInterval()
    {
        using var parent = LuaState.Create();
        var positions = new Dictionary<LuaState, List<int>>();
        var hook = new LuaFunction(
            (c, ct) =>
            {
                if (!positions.TryGetValue(c.State, out var list))
                    positions.Add(c.State, list = new());
                list.Add(c.State.GetCallStackFrames()[^1].CallerInstructionIndex);
                return new(c.Return());
            }
        );
        parent.SetHook(hook, "", 4);
        await parent.DoStringAsync("return 7");
        Assert.That(positions.ContainsKey(parent), Is.False);
        using var inherited = parent.CreateThread();
        using var explicitlyConfigured = parent.CreateThread();
        explicitlyConfigured.SetHook(hook, "", 4);
        await inherited.DoStringAsync(Loop);
        await explicitlyConfigured.DoStringAsync(Loop);
        Assert.That(positions.ContainsKey(inherited), Is.True);
        Assert.That(positions[inherited], Is.EqualTo(positions[explicitlyConfigured]));
    }

    [Test]
    public async Task HostHook_ChildCreatedInsideCallback_IsNotSuppressed()
    {
        using var parent = LuaState.Create();
        var childFires = 0;
        var parentFires = 0;
        parent.SetHook(
            new(
                async (c, ct) =>
                {
                    if (c.State == parent)
                    {
                        parentFires++;
                        if (parentFires == 1)
                        {
                            using var child = parent.CreateThread();
                            var nested = await child.DoStringAsync(Loop, "child.lua", ct);
                            Assert.That(nested[0], Is.EqualTo((LuaValue)5050));
                        }
                    }
                    else
                        childFires++;
                    return c.Return();
                }
            ),
            "",
            4
        );
        await parent.DoStringAsync(Loop);
        Assert.That(parentFires, Is.EqualTo(51));
        Assert.That(childFires, Is.EqualTo(51));
    }

    [Test]
    public async Task HostHook_LuaClosureInstalledFromCSharp_IsInherited()
    {
        using var parent = LuaState.Create();
        LuaState? child = null;
        var fires = 0;
        parent.Environment["mark"] = new LuaFunction(
            (c, ct) =>
            {
                if (c.State == child)
                    fires++;
                return new(c.Return());
            }
        );
        var hook = (await parent.DoStringAsync("return function() mark() end"))[0]
            .Read<LuaFunction>();
        parent.SetHook(hook, "", 4);
        using (child = parent.CreateThread())
        {
            await child.DoStringAsync(Loop);
            Assert.That(fires, Is.EqualTo(51));
        }
    }

    [Test]
    public async Task HostHook_InheritedBudgetError_StopsProtectedChild()
    {
        using var parent = LuaState.Create();
        parent.OpenCoroutineLibrary();
        var fires = 0;
        parent.SetHook(
            new(
                (c, ct) =>
                {
                    if (c.State != parent && ++fires == 3)
                        throw new LuaRuntimeException(c.State, "budget");
                    return new(c.Return());
                }
            ),
            "",
            4
        );
        var result = await parent.DoStringAsync(
            "return coroutine.resume(coroutine.create(function() " + Loop + " end))"
        );
        Assert.That(result, Is.EqualTo(new LuaValue[] { false, "budget" }));
        Assert.That(fires, Is.EqualTo(3));
    }

    [Test]
    public async Task HostHook_InheritedCancellation_PropagatesThroughPCallAndResume()
    {
        using var parent = LuaState.Create();
        using var cancellation = new CancellationTokenSource();
        parent.OpenBasicLibrary();
        parent.OpenCoroutineLibrary();
        LuaState? child = null;
        parent.SetHook(
            new(
                (c, ct) =>
                {
                    if (c.State != parent)
                    {
                        child = c.State;
                        cancellation.Cancel();
                        ct.ThrowIfCancellationRequested();
                    }
                    return new(c.Return());
                }
            ),
            "",
            4
        );
        try
        {
            Assert.ThrowsAsync<LuaCanceledException>(async () =>
                await parent.DoStringAsync(
                    "return pcall(coroutine.resume,coroutine.create(function() " + Loop + " end))",
                    "cancel.lua",
                    cancellation.Token
                )
            );
            Assert.That(child, Is.Not.Null);
            Assert.That(parent.CallStackFrameCount, Is.Zero);
            Assert.That(child!.CallStackFrameCount, Is.Zero);
        }
        finally
        {
            child?.Dispose();
        }
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task LuaDebugHook_CallbackIsThreadLocal_MaskAndIntervalAreInherited(
        bool hostCallback
    )
    {
        using var parent = LuaState.Create();
        parent.OpenCoroutineLibrary();
        parent.OpenDebugLibrary();
        var childFires = 0;
        parent.Environment["hostHook"] = new LuaFunction(
            (c, ct) =>
            {
                if (c.State != parent)
                    childFires++;
                return new(c.Return());
            }
        );
        var source =
            """
                local parent = coroutine.running()
                local childFires = 0
                local hook = function()
                    if coroutine.running() ~= parent then childFires = childFires + 1 end
                end
                """
            + (hostCallback ? "\nhook = hostHook\n" : "\n")
            + """
                debug.sethook(hook, "lcr", 4)
                local child = coroutine.create(function()
                    local grandchild = coroutine.create(function() return 7 end)
                    local gh,gm,gc = debug.gethook(grandchild)
                    return gh,gm,gc
                end)
                local h,m,c = debug.gethook(child)
                local ok,gh,gm,gc = coroutine.resume(child)
                debug.sethook()
                return h,m,c,ok,gh,gm,gc,childFires
                """;
        var result = await parent.DoStringAsync(source);
        Assert.That(
            result,
            Is.EqualTo(new LuaValue[] { LuaValue.Nil, "crl", 4, true, LuaValue.Nil, "crl", 4, 0 })
        );
        Assert.That(childFires, Is.Zero);
    }

    [Test]
    public async Task LuaDebugHook_ExplicitChildHook_DoesNotPropagateCallbackToGrandchild()
    {
        using var parent = LuaState.Create();
        parent.OpenBasicLibrary();
        parent.OpenCoroutineLibrary();
        parent.OpenDebugLibrary();
        var result = await parent.DoStringAsync(
            """
            local parentFires, childFires, grandchildFires = 0,0,0
            local parent = coroutine.running()
            local child
            local parentHook = function() parentFires = parentFires + 1 end
            local childHook = function()
                if coroutine.running() == child then childFires = childFires + 1
                else grandchildFires = grandchildFires + 1 end
            end
            debug.sethook(parentHook, "", 4)
            child = coroutine.create(function()
                local grandchild = coroutine.create(function()
                    local x=0 for i=1,100 do x=x+i end return x
                end)
                return coroutine.resume(grandchild)
            end)
            debug.sethook(child, childHook, "", 4)
            local h,m,c = debug.gethook(child)
            local ok,innerOk,x = coroutine.resume(child)
            debug.sethook()
            return h == childHook,m,c,ok,innerOk,x,parentFires > 0,childFires > 0,grandchildFires
            """
        );
        Assert.That(
            result,
            Is.EqualTo(new LuaValue[] { true, "", 4, true, true, 5050, true, true, 0 })
        );
    }

    static LuaFunction Counter(Dictionary<LuaState, int> counts) =>
        new(
            (c, ct) =>
            {
                counts[c.State] = counts.GetValueOrDefault(c.State) + 1;
                return new(c.Return());
            }
        );
}
