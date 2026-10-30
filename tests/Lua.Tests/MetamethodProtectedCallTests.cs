using Lua.Platforms;
using Lua.Standard;

namespace Lua.Tests;

[TestFixture(LuaStringMode.Utf16)]
[TestFixture(LuaStringMode.Bytes)]
public class MetamethodProtectedCallTests(LuaStringMode mode)
{
    LuaState CreateState()
    {
        var state = LuaState.Create(
            mode == LuaStringMode.Bytes ? LuaPlatform.DefaultByteStrings : LuaPlatform.Default
        );
        state.OpenStandardLibraries();
        return state;
    }

    static readonly string[] HostOperations =
    [
        "GetTable",
        "SetTable",
        "Add",
        "Sub",
        "Mul",
        "Div",
        "Mod",
        "Pow",
        "Unm",
        "Len",
        "LessThan",
        "LessThanOrEquals",
        "Equals",
        "Concat",
        "ConcatStack",
    ];

    [Test, Combinatorial]
    public async Task HostCallsDoNotHandleCaughtMetamethodErrors(
        [ValueSource(nameof(HostOperations))] string operation,
        [Values(false, true)] bool asynchronous
    )
    {
        using var state = CreateState();
        var suspension = new ManualSuspension();
        state.Environment["pause"] = new LuaFunction(
            async (context, _) =>
            {
                if (asynchronous)
                    await suspension;
                return context.Return();
            }
        );
        var fail = state.Load("pause();error('caught metamethod failure',0)", "@metamethod");
        var meta = new LuaTable();
        foreach (
            var name in new[]
            {
                "__index",
                "__newindex",
                "__add",
                "__sub",
                "__mul",
                "__div",
                "__mod",
                "__pow",
                "__unm",
                "__len",
                "__lt",
                "__le",
                "__eq",
                "__concat",
            }
        )
            meta[name] = fail;
        var left = new LuaTable { Metatable = meta };
        var right = new LuaTable { Metatable = meta };
        state.Environment["recover"] = new LuaFunction(
            async (context, token) =>
            {
                try
                {
                    await InvokeAsync(context.State, operation, left, right, token);
                }
                catch (LuaRuntimeException) when (context.ArgumentCount == 0) { }
                return context.Return();
            }
        );
        var task = state
            .DoStringAsync(
                """
                local seen={}
                local handler=function(e) seen[#seen+1]=e;return e end
                local ok,value=xpcall(function() recover();return 42 end,handler)
                assert(ok and value==42 and #seen==0)
                ok,value=xpcall(function() recover();error('real failure',0) end,handler)
                local propagated,message=xpcall(function() recover(true) end,handler)
                assert(not propagated and message=='caught metamethod failure')
                return ok,value,#seen,seen[1],seen[2]
                """
            )
            .AsTask();
        if (asynchronous)
        {
            suspension.Resume();
            suspension.Resume();
            suspension.Resume();
        }
        Assert.That(
            await task,
            Is.EqualTo(
                new LuaValue[]
                {
                    false,
                    "real failure",
                    2,
                    "real failure",
                    "caught metamethod failure",
                }
            )
        );
        Assert.That(state.ErrorHandlerIndex, Is.Zero);
        Assert.That(state.Stack.Count, Is.Zero);
        Assert.That(state.CallStackFrameCount, Is.Zero);
    }

    static async ValueTask InvokeAsync(
        LuaState state,
        string operation,
        LuaValue left,
        LuaValue right,
        CancellationToken token
    )
    {
        switch (operation)
        {
            case "GetTable":
                await state.GetTableAsync(left, "missing", token);
                break;
            case "SetTable":
                await state.SetTableAsync(left, "missing", 42, token);
                break;
            case "Add":
                await state.AddAsync(left, right, token);
                break;
            case "Sub":
                await state.SubAsync(left, right, token);
                break;
            case "Mul":
                await state.MulAsync(left, right, token);
                break;
            case "Div":
                await state.DivAsync(left, right, token);
                break;
            case "Mod":
                await state.ModAsync(left, right, token);
                break;
            case "Pow":
                await state.PowAsync(left, right, token);
                break;
            case "Unm":
                await state.UnmAsync(left, token);
                break;
            case "Len":
                await state.LenAsync(left, token);
                break;
            case "LessThan":
                await state.LessThanAsync(left, right, token);
                break;
            case "LessThanOrEquals":
                await state.LessThanOrEqualsAsync(left, right, token);
                break;
            case "Equals":
                await state.EqualsAsync(left, right, token);
                break;
            case "Concat":
                await state.ConcatAsync([left, right], token);
                break;
            case "ConcatStack":
                state.Stack.PushRange(left, right);
                await state.ConcatAsync(2, token);
                break;
        }
    }

    [Test, Combinatorial]
    public async Task LuaHandlersCanInspectFailingManagedMetamethodFrames(
        [Values("index", "newindex", "print", "sort", "gsub", "concat")] string site,
        [Values(false, true)] bool asynchronous
    )
    {
        using var state = CreateState();
        var suspension = new ManualSuspension();
        state.Environment["fail"] = asynchronous
            ? new LuaFunction(
                async (context, _) =>
                {
                    await suspension;
                    throw new LuaRuntimeException(context.State, "managed failure", 0);
                }
            )
            : state.Environment["error"];
        var source = site switch
        {
            "index" => "setmetatable(package.loaded,{__index=fail});require('test')",
            "newindex" =>
                "package.preload.test=function()return 42 end;setmetatable(package.loaded,{__newindex=fail});require('test')",
            "print" => "tostring=nil;setmetatable(_G,{__index=fail});print(42)",
            "sort" =>
                "local meta={__lt=fail};table.sort({setmetatable({},meta),setmetatable({},meta)})",
            "gsub" => "string.gsub('a','.',setmetatable({},{__index=fail}))",
            _ =>
                "local meta={__concat=fail};local a,b=setmetatable({},meta),setmetatable({},meta);return a..b",
        };
        state.Environment["operation"] = state.Load(source, "@operation");
        var task = state
            .DoStringAsync(
                """
                return xpcall(operation,function(e)
                    for level=1,20 do
                        local info=debug.getinfo(level,'f')
                        if info and info.func==fail then return true end
                    end
                    return false
                end)
                """
            )
            .AsTask();
        if (asynchronous)
            suspension.Resume();
        Assert.That(await task, Is.EqualTo(new LuaValue[] { false, true }));
        Assert.That(state.ErrorHandlerIndex, Is.Zero);
        Assert.That(state.Stack.Count, Is.Zero);
        Assert.That(state.CallStackFrameCount, Is.Zero);
    }
}
