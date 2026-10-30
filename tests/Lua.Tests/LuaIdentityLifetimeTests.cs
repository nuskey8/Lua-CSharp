using System.Runtime.CompilerServices;
using Lua.Platforms;
using Lua.Runtime;
using Lua.Standard;

namespace Lua.Tests;

[TestFixture(LuaStringMode.Utf16)]
[TestFixture(LuaStringMode.Bytes)]
public class LuaIdentityLifetimeTests(LuaStringMode mode)
{
    LuaState CreateState()
    {
        var state = LuaState.Create(
            mode == LuaStringMode.Bytes ? LuaPlatform.DefaultByteStrings : LuaPlatform.Default
        );
        state.OpenStandardLibraries();
        return state;
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task ReferenceKeysSurviveMutableHostHashCodes(bool fullUserData)
    {
        using var state = CreateState();
        var key = new MutableKey();
        state.Environment["key"] = KeyValue(key, fullUserData);
        state.Environment["other"] = KeyValue(new MutableKey(), fullUserData);
        state.Environment["mutate"] = new LuaFunction(
            (context, _) =>
            {
                key.Code = 2;
                return new(context.Return());
            }
        );
        var result = await state.DoStringAsync(
            """
            local t={[key]=42,[other]=99}
            mutate()
            local value=t[key]
            t[key]=nil
            local count=0
            for k,v in pairs(t) do count=count+1 end
            return value,t[other],count,key==other
            """
        );
        Assert.That(result, Is.EqualTo(new LuaValue[] { 42, 99, 1, false }));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void DeletedReferenceKeysAndValuesAreCollectibleAfterHostMutation(bool fullUserData)
    {
        using var state = CreateState();
        var (key, payload) = InsertMutateAndDelete(state, fullUserData);
        Collect();
        Assert.That(key.IsAlive, Is.False);
        Assert.That(payload.IsAlive, Is.False);
        Assert.That(state.Environment["kept"].Read<LuaTable>().HashMapCount, Is.Zero);
        GC.KeepAlive(state);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static (WeakReference Key, WeakReference Payload) InsertMutateAndDelete(
        LuaState state,
        bool fullUserData
    )
    {
        var key = new MutableKey();
        var payload = new object();
        state.Environment["key"] = KeyValue(key, fullUserData);
        state.Environment["payload"] = LuaValue.FromObject(payload);
        state.Environment["mutate"] = new LuaFunction(
            (context, _) =>
            {
                key.Code = 2;
                return new(context.Return());
            }
        );
        state
            .DoStringAsync(
                "kept={};kept[key]=payload;mutate();kept[key]=nil;key=nil;payload=nil;mutate=nil"
            )
            .GetAwaiter()
            .GetResult();
        return (new WeakReference(key), new WeakReference(payload));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void UpValueIdsDoNotKeepDiscardedCapturesAlive(bool coroutine)
    {
        using var state = CreateState();
        var payload = SaveCaptureId(state, coroutine);
        Collect();
        Assert.That(payload.IsAlive, Is.False);
        Assert.That(state.Environment["identity"].Type, Is.EqualTo(LuaValueType.LightUserData));
        GC.KeepAlive(state);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static WeakReference SaveCaptureId(LuaState state, bool coroutine)
    {
        var payload = new object();
        state.Environment["payload"] = LuaValue.FromObject(payload);
        var body = """
            local keep=payload
            payload=nil
            local f=function() return keep end
            identity=debug.upvalueid(f,1)
            """;
        state
            .DoStringAsync(
                coroutine
                    ? "local co=coroutine.create(function() "
                        + body
                        + ";coroutine.yield();return keep end);assert(coroutine.resume(co))"
                    : body
            )
            .GetAwaiter()
            .GetResult();
        return new WeakReference(payload);
    }

    [Test]
    public async Task UpValueIdsRemainStableForSharedAndJoinedCaptures()
    {
        using var state = CreateState();
        state.Environment["collect"] = new LuaFunction(
            (context, _) =>
            {
                Collect();
                return new(context.Return());
            }
        );
        var result = await state.DoStringAsync(
            """
            local n,m=1,2
            local f=function() return n end
            local g=function() return n end
            local h=function() return m end
            local identity=debug.upvalueid(f,1)
            local before=debug.upvalueid(h,1)
            collect()
            local stable=identity==debug.upvalueid(f,1)
            local shared=identity==debug.upvalueid(g,1)
            debug.upvaluejoin(h,1,f,1)
            collect()
            n=3
            return stable,shared,before~=identity,debug.upvalueid(h,1)==identity,f(),g(),h()
            """
        );
        Assert.That(result, Is.EqualTo(new LuaValue[] { true, true, true, true, 3, 3, 3 }));
    }

    [Test]
    public async Task ManagedClosureUpValuesHaveStableSlotIdentitiesAndMetadata()
    {
        using var state = CreateState();
        state.Environment["first"] = new CSharpClosure(
            "first",
            [42, 42],
            static (context, _) => new(context.Return())
        );
        state.Environment["second"] = new CSharpClosure(
            "second",
            [42],
            static (context, _) => new(context.Return())
        );
        await state.DoStringAsync(
            """
            assert(debug.getinfo(first,'u').nups==2 and debug.getinfo(second,'u').nups==1)
            assert(debug.getinfo(require,'u').nups==1)
            local id=debug.upvalueid(first,1)
            assert(type(id)=='userdata' and id==debug.upvalueid(first,1))
            assert(id~=debug.upvalueid(first,2) and id~=debug.upvalueid(second,1))
            assert(debug.setupvalue(first,1,43)=='')
            assert(id==debug.upvalueid(first,1))
            local name,value=debug.getupvalue(first,1)
            assert(name=='' and value==43)
            assert(debug.upvalueid(require,1)~=debug.upvalueid(package.searchers[2],1))
            """
        );
    }

    [Test]
    public async Task UpValueIdentityAndJoinRejectInvalidArguments()
    {
        using var state = CreateState();
        await state.DoStringAsync(
            """
            local value=42
            local function f() return value end
            local function checkError(argument,reason,func,...)
                local ok,e=pcall(func,...)
                assert(not ok and e:find('bad argument #'..argument,1,true) and e:find(reason,1,true),e)
            end
            for _,func in ipairs({f,require,print}) do
                local count=debug.getinfo(func,'u').nups
                for _,index in ipairs({-1,0,count+1}) do
                    checkError(2,"invalid upvalue index",debug.upvalueid,func,index)
                end
            end
            checkError(1,"Lua function expected",debug.upvaluejoin,require,1,f,1)
            checkError(3,"Lua function expected",debug.upvaluejoin,f,1,require,1)
            checkError(2,"invalid upvalue index",debug.upvaluejoin,require,0,f,1)
            checkError(4,"invalid upvalue index",debug.upvaluejoin,require,1,f,0)
            checkError(2,"invalid upvalue index",debug.upvaluejoin,f,0,f,1)
            checkError(4,"invalid upvalue index",debug.upvaluejoin,f,1,f,0)
            assert(f()==42)
            """
        );
    }

    [Test]
    public void ManagedUpValueIdsDoNotKeepDiscardedCapturesAlive()
    {
        using var state = CreateState();
        var payload = SaveManagedCaptureId(state);
        Collect();
        Assert.That(payload.IsAlive, Is.False);
        Assert.That(state.Environment["identity"].Type, Is.EqualTo(LuaValueType.LightUserData));
        GC.KeepAlive(state);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static WeakReference SaveManagedCaptureId(LuaState state)
    {
        var payload = new object();
        state.Environment["capture"] = new CSharpClosure(
            "capture",
            [LuaValue.FromObject(payload)],
            static (context, _) => new(context.Return())
        );
        state
            .DoStringAsync("identity=debug.upvalueid(capture,1);capture=nil")
            .GetAwaiter()
            .GetResult();
        return new WeakReference(payload);
    }

    static LuaValue KeyValue(MutableKey key, bool fullUserData) =>
        fullUserData ? LuaValue.FromUserData(key) : LuaValue.FromObject(key);

    static void Collect()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    sealed class MutableKey : ILuaUserData
    {
        public int Code = 1;
        public LuaTable? Metatable { get; set; }

        public override int GetHashCode() => Code;
    }
}
