using Lua.Standard;

namespace Lua.Tests;

public class TableLibraryMetatableTests
{
    [TestCase("local a,b,c = table.unpack(t); assert(a == 30 and b == 10 and c == nil)")]
    [TestCase("assert(table.concat(t, ',') == '30,10')")]
    [TestCase(
        "table.insert(t, 5); assert(t[1] == 30 and t[2] == 10 and t[3] == 5 and t[4] == nil)"
    )]
    [TestCase(
        "table.insert(t, 1, 5); assert(t[1] == 5 and t[2] == 30 and t[3] == 10 and t[4] == nil)"
    )]
    [TestCase("assert(table.remove(t) == 10); assert(t[1] == 30 and t[2] == nil and t[3] == 99)")]
    [TestCase(
        "assert(table.remove(t, 1) == 30); assert(t[1] == 10 and t[2] == nil and t[3] == 99)"
    )]
    [TestCase("table.sort(t); assert(t[1] == 10 and t[2] == 30 and t[3] == 99)")]
    public async Task Test_LengthControlsOperationRange(string operation)
    {
        var state = LuaState.Create();
        state.OpenStandardLibraries();
        await state.DoStringAsync(
            """
            local calls = 0
            local t
            t = setmetatable({30, 10, 99}, {__len = function(self)
                assert(self == t)
                calls = calls + 1
                return 2
            end})
            """
                + operation
                + "; assert(calls == 1)"
        );
    }

    [TestCase("table.unpack(t, 1, 2)")]
    [TestCase("table.concat(t, ',', 1, 2)")]
    public async Task Test_ExplicitEndDoesNotEvaluateLength(string operation)
    {
        var state = LuaState.Create();
        state.OpenStandardLibraries();
        await state.DoStringAsync(
            "local t = setmetatable({1,2}, {__len = function() error('unexpected length') end}); "
                + operation
        );
    }

    [TestCase("assert(select('#', table.unpack(t, nil, nil)) == 1)")]
    [TestCase("assert(table.concat(t, nil, nil, nil) == '1')")]
    public async Task Test_NilEndUsesLength(string operation)
    {
        var state = LuaState.Create();
        state.OpenStandardLibraries();
        await state.DoStringAsync(
            "local t = setmetatable({1,2}, {__len = function() return 1 end}); " + operation
        );
    }

    [TestCase("table.unpack(t)")]
    [TestCase("table.concat(t)")]
    [TestCase("table.insert(t, 4)")]
    [TestCase("table.remove(t)")]
    [TestCase("table.sort(t)")]
    public async Task Test_LengthErrorIsProtected(string operation)
    {
        var state = LuaState.Create();
        state.OpenStandardLibraries();
        await state.DoStringAsync(
            """
            local t = setmetatable({1,2,3}, {__len = function() error('length failed', 0) end})
            local ok, err = pcall(function()
            """
                + operation
                + " end); assert(not ok and string.find(err, 'length failed', 1, true))"
        );
        Assert.That((await state.DoStringAsync("return 42"))[0].Read<double>(), Is.EqualTo(42));
    }

    [TestCase("table.unpack(t)")]
    [TestCase("table.concat(t)")]
    [TestCase("table.insert(t, 4)")]
    [TestCase("table.remove(t)")]
    [TestCase("table.sort(t)")]
    public async Task Test_InvalidLengthResult(string operation)
    {
        var state = LuaState.Create();
        state.OpenStandardLibraries();
        await state.DoStringAsync(
            """
            local t = setmetatable({1,2,3}, {__len = function() return {} end})
            local ok, err = pcall(function()
            """
                + operation
                + " end); assert(not ok and string.find(err, 'length', 1, true))"
        );
    }

    [Test]
    public async Task Test_RawElementAccessAndLengthBeyondStorage()
    {
        var state = LuaState.Create();
        state.OpenStandardLibraries();
        await state.DoStringAsync(
            """
            local mt = {
                __len = function() return 3 end,
                __index = function() error('unexpected index') end,
                __newindex = function() error('unexpected newindex') end
            }
            local t = setmetatable({}, mt)
            local values = table.pack(table.unpack(t))
            assert(values.n == 3 and values[1] == nil and values[3] == nil)
            table.insert(t, 7)
            assert(rawget(t, 4) == 7)
            assert(table.remove(t, 4) == 7 and rawget(t, 4) == nil)
            table.sort(t, function(a,b) return false end)
            assert(next(t) == nil)
            """
        );
    }

    [Test]
    public async Task Test_LengthCanMutateTable()
    {
        var state = LuaState.Create();
        state.OpenStandardLibraries();
        await state.DoStringAsync(
            """
            local t = setmetatable({}, {__len = function(self)
                self[1] = 2
                self[2] = 1
                return 2
            end})
            table.sort(t)
            assert(t[1] == 1 and t[2] == 2)
            """
        );
    }

    [Test]
    public async Task Test_EmptyLengthRemoveAndInsert()
    {
        var state = LuaState.Create();
        state.OpenStandardLibraries();
        await state.DoStringAsync(
            """
            local t = setmetatable({99}, {__len = function() return 0 end})
            t[0] = 42
            assert(table.remove(t) == 42 and rawget(t,0) == nil and t[1] == 99)
            assert(not pcall(table.insert, t, 2, 7))
            table.insert(t, 7)
            assert(t[1] == 7 and t[2] == nil)
            """
        );
    }

    [TestCase("local a,b,c = table.unpack(t); assert(a == 2 and b == 1 and c == nil)")]
    [TestCase("assert(table.concat(t, ',') == '2,1')")]
    [TestCase("table.insert(t, 3); assert(t[3] == 3 and t[4] == nil)")]
    [TestCase("assert(table.remove(t) == 1 and t[2] == nil and t[3] == 99)")]
    [TestCase("table.sort(t); assert(t[1] == 1 and t[2] == 2 and t[3] == 99)")]
    public async Task Test_AsyncLength(string operation)
    {
        var state = LuaState.Create();
        state.OpenStandardLibraries();
        var calls = 0;
        state.Environment["length"] = new LuaFunction(
            "length",
            async (context, ct) =>
            {
                await Task.Yield();
                ct.ThrowIfCancellationRequested();
                calls++;
                return context.Return(2);
            }
        );
        await state.DoStringAsync(
            "local t = setmetatable({2,1,99}, {__len = length}); " + operation
        );
        Assert.That(calls, Is.EqualTo(1));
    }
}
