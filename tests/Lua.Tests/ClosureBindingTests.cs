using Lua.Platforms;
using Lua.Runtime;
using Lua.Standard;

namespace Lua.Tests;

[TestFixture(LuaStringMode.Utf16)]
[TestFixture(LuaStringMode.Bytes)]
public class ClosureBindingTests(LuaStringMode stringMode)
{
    LuaState CreateState() =>
        LuaState.Create(
            stringMode == LuaStringMode.Bytes ? LuaPlatform.DefaultByteStrings : LuaPlatform.Default
        );

    [TestCase(false)]
    [TestCase(true)]
    public async Task IndexingCapturedEnvironmentReportsLuaError(bool setter)
    {
        using var state = CreateState();
        state.OpenStandardLibraries();
        var results = await state.DoStringAsync(
            "local protected=pcall; local a,b,c,d=1,2,3,4; local _ENV=1; local function f() "
                + (setter ? "missing=42" : "return missing")
                + " end; return protected(f)"
        );
        Assert.That(results[0], Is.EqualTo(new LuaValue(false)));
        Assert.That(results[1].Read<string>(), Does.Contain("attempt to index a number value"));
        Assert.That(results[1].Read<string>(), Does.Contain("upvalue '_ENV'"));
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task NestedClosureCapturesLocalEnvironmentAtRegisterZero(bool bytecode)
    {
        using var state = CreateState();
        state.Environment["value"] = 42;
        var environment = new LuaTable { ["value"] = 99 };
        var chunk = state.Load(
            "local _ENV={value=7}; return function() return value end",
            "local-environment",
            environment
        );
        if (bytecode)
            chunk = state.Load(chunk.Proto.ToBytecode(), environment: environment);
        var nested = (await state.ExecuteAsync(chunk))[0].Read<LuaClosure>();
        Assert.That((await state.ExecuteAsync(nested))[0], Is.EqualTo(new LuaValue(7)));
        Assert.That(state.Environment["value"], Is.EqualTo(new LuaValue(42)));
    }

    [Test]
    public async Task LoadedChunksKeepIndependentEnvironmentCells()
    {
        using var state = CreateState();
        state.Environment["value"] = 42;
        var reader = state.Load("return value", "reader");
        var setter = state.Load("_ENV={value=7}; return value", "setter");
        Assert.That((await state.ExecuteAsync(setter))[0], Is.EqualTo(new LuaValue(7)));
        Assert.That((await state.ExecuteAsync(reader))[0], Is.EqualTo(new LuaValue(42)));
        Assert.That((await state.DoStringAsync("return value"))[0], Is.EqualTo(new LuaValue(42)));
    }

    [Test]
    public async Task CurrentEnvironmentCanFollowAnotherCapturedVariable()
    {
        using var state = CreateState();
        var environment = new LuaTable
        {
            ["read"] = new LuaFunction(
                (context, _) => new(context.Return(context.State.GetCurrentEnvironment()))
            ),
        };
        var chunk = state.Load(
            "local x=1; return function() local y=x; return y,read() end",
            "current-environment",
            environment
        );
        var nested = (await state.ExecuteAsync(chunk))[0].Read<LuaClosure>();
        var results = await state.ExecuteAsync(nested);
        Assert.That(results[0], Is.EqualTo(new LuaValue(1)));
        Assert.That(results[1].Read<LuaTable>(), Is.SameAs(environment));
    }

    [TestCase(false, false)]
    [TestCase(true, false)]
    [TestCase(false, true)]
    [TestCase(true, true)]
    public async Task ReloadedFunctionInitializesAllUpvaluesWithoutCapturingCaller(
        bool duringCall,
        bool explicitEnvironment
    )
    {
        using var state = CreateState();
        var original = (
            await state.DoStringAsync(
                "local first,second=11,22; return function() return first,second end"
            )
        )[0]
            .Read<LuaClosure>();
        var bytes = original.Proto.ToBytecode();
        var environment = explicitEnvironment ? new LuaTable() : null;
        var initializedClosed = false;
        LuaClosure Load()
        {
            var result = state.Load(bytes, environment: environment);
            initializedClosed = result.UpValues.ToArray().All(value => value.IsClosed);
            return result;
        }

        LuaClosure loaded;
        if (duringCall)
        {
            state.Environment["reload"] = new LuaFunction(
                (context, _) => new(context.Return(Load()))
            );
            loaded = (await state.DoStringAsync("return reload()"))[0].Read<LuaClosure>();
        }
        else
        {
            loaded = Load();
        }

        Assert.That(loaded.UpValues.Length, Is.EqualTo(2));
        Assert.That(initializedClosed, Is.True);
        var results = await state.ExecuteAsync(loaded);
        Assert.That(
            results[0],
            Is.EqualTo(environment is null ? LuaValue.Nil : new LuaValue(environment))
        );
        Assert.That(results[1], Is.EqualTo(LuaValue.Nil));
        Assert.That((await state.DoStringAsync("return 42"))[0], Is.EqualTo(new LuaValue(42)));
    }

    [Test]
    public async Task ReloadedFunctionWithOneUpvalueDefaultsToGlobalEnvironment()
    {
        using var state = CreateState();
        var original = (
            await state.DoStringAsync("local value=42; return function() return value end")
        )[0]
            .Read<LuaClosure>();
        var loaded = state.Load(original.Proto.ToBytecode());
        var results = await state.ExecuteAsync(loaded);
        Assert.That(results[0].Read<LuaTable>(), Is.SameAs(state.Environment));
    }

    [Test]
    public async Task FunctionWithoutUpvaluesStaysWithoutUpvaluesWhenReloaded()
    {
        using var state = CreateState();
        var original = (await state.DoStringAsync("return function() return 42 end"))[0]
            .Read<LuaClosure>();
        var loaded = state.Load(original.Proto.ToBytecode());
        Assert.That(loaded.UpValues.Length, Is.Zero);
        Assert.That((await state.ExecuteAsync(loaded))[0], Is.EqualTo(new LuaValue(42)));
    }
}
