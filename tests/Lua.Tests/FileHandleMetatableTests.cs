using System.Runtime.CompilerServices;
using Lua.Platforms;
using Lua.Standard;

namespace Lua.Tests;

[TestFixture(LuaStringMode.Utf16)]
[TestFixture(LuaStringMode.Bytes)]
public class FileHandleMetatableTests(LuaStringMode mode)
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
    public async Task IndependentStatesHaveIndependentFileMetatables()
    {
        using var first = CreateState();
        using var second = CreateState();
        await first.DoStringAsync("getmetatable(io.stdout).tag=42");
        var result = await second.DoStringAsync("return getmetatable(io.stdout).tag");
        Assert.That(result, Is.EqualTo(new LuaValue[] { LuaValue.Nil }));
    }

    [Test]
    public void DiscardedStateAndItsFileMetatableValuesAreCollectible()
    {
        var (state, payload) = CreateAndDiscard();
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        Assert.That(state.IsAlive, Is.False);
        Assert.That(payload.IsAlive, Is.False);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    (WeakReference State, WeakReference Payload) CreateAndDiscard()
    {
        using var state = CreateState();
        var payload = new object();
        state.Environment["payload"] = LuaValue.FromObject(payload);
        state
            .DoStringAsync(
                "local mt=getmetatable(io.stdout);mt.payload=payload;mt.owner=coroutine.running();payload=nil"
            )
            .GetAwaiter()
            .GetResult();
        return (new WeakReference(state), new WeakReference(payload));
    }

    [Test]
    public async Task StateOwnedHandlesShareAMetatableAndReplacementAffectsOnlyOneHandle()
    {
        using var state = CreateState();
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, "line\n");
            state.Environment["path"] = state.Platform.EncodeText(path);
            var result = await state.DoStringAsync(
                """
                local shared=getmetatable(io.stdout)
                assert(shared==getmetatable(io.stdin) and shared==getmetatable(io.stderr))
                local a=assert(io.open(path,'r'))
                local b=assert(io.tmpfile())
                io.input(path)
                local c=io.input()
                io.output(path..'.out')
                local d=io.output()
                local lines=io.lines(path)
                local _,e=debug.getupvalue(lines,1)
                local files={a,b,c,d,e}
                for _,file in ipairs(files) do assert(getmetatable(file)==shared) end
                shared.tag=42
                local replacement={}
                debug.setmetatable(a,replacement)
                local isolated=getmetatable(a)==replacement and getmetatable(b)==shared
                local tag=getmetatable(b).tag
                debug.setmetatable(a,shared)
                for _,file in ipairs(files) do file:close() end
                return isolated,tag
                """
            );
            Assert.That(result, Is.EqualTo(new LuaValue[] { true, 42 }));
        }
        finally
        {
            File.Delete(path);
            File.Delete(path + ".out");
        }
    }
}
