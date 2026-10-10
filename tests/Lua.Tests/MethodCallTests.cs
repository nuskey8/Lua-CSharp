using Lua.Standard;

namespace Lua.Tests;

public class MethodCallTests
{
    [TestCase(false, false, false)]
    [TestCase(false, false, true)]
    [TestCase(false, true, false)]
    [TestCase(false, true, true)]
    [TestCase(true, false, false)]
    [TestCase(true, false, true)]
    [TestCase(true, true, false)]
    [TestCase(true, true, true)]
    public async Task LuaIndex_PreservesReceiver(bool tailCall, bool chained, bool hooks)
    {
        using var state = LuaState.Create();
        state.OpenStandardLibraries();
        state.Environment["tailCall"] = tailCall;
        state.Environment["chained"] = chained;
        state.Environment["hooks"] = hooks;
        await state.DoStringAsync(
            """
            local method = function(self, value) return self, value end
            local lookup = function() return method end
            local index = function()
                if tailCall then return lookup() end
                local result = lookup()
                return result
            end
            if chained then index = setmetatable({}, {__index = index}) end
            local object = setmetatable({}, {__index = index})
            if hooks then debug.sethook(function() end, "cr") end
            local self, value = object:method(42)
            debug.sethook()
            assert(self == object, "method received the wrong self")
            assert(value == 42)
            """
        );
    }

    [TestCase(false, false)]
    [TestCase(false, true)]
    [TestCase(true, false)]
    [TestCase(true, true)]
    public async Task CSharpIndex_PreservesReceiver(bool asynchronous, bool growStack)
    {
        using var state = LuaState.Create();
        state.OpenStandardLibraries();
        var method = state.Load("return ...", "method.lua");
        state.Environment["index"] = new LuaFunction("index", asynchronous ? LookupAsync : Lookup);
        await state.DoStringAsync(
            """
            local object = setmetatable({}, {__index = index})
            local self, value = object:method(42)
            assert(self == object, "method received the wrong self")
            assert(value == 42)
            """
        );

        ValueTask<int> Lookup(LuaFunctionExecutionContext context, CancellationToken ct)
        {
            if (growStack)
                context.State.Stack.EnsureCapacity(4096);
            return new(context.Return(method));
        }

        async ValueTask<int> LookupAsync(LuaFunctionExecutionContext context, CancellationToken ct)
        {
            await Task.Yield();
            return await Lookup(context, ct);
        }
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task IndexChangingReceiverRegister_PreservesOriginalReceiver(bool hooks)
    {
        using var state = LuaState.Create();
        state.OpenStandardLibraries();
        state.Environment["hooks"] = hooks;
        await state.DoStringAsync(
            """
            local object
            local original = setmetatable({}, {
                __index = function()
                    object = {}
                    return function(self) return self end
                end
            })
            object = original
            if hooks then debug.sethook(function() end, "cr") end
            assert(object:method() == original)
            debug.sethook()
            assert(object ~= original)
            """
        );
    }

    [Test]
    public async Task YieldingIndex_PreservesReceiver()
    {
        using var state = LuaState.Create();
        state.OpenStandardLibraries();
        await state.DoStringAsync(
            """
            local object = setmetatable({}, {
                __index = function()
                    coroutine.yield("lookup")
                    return function(self, value) return self, value end
                end
            })
            local thread = coroutine.create(function()
                local self, value = object:method(42)
                assert(self == object)
                assert(value == 42)
                return "done"
            end)
            local ok, result = coroutine.resume(thread)
            assert(ok and result == "lookup")
            ok, result = coroutine.resume(thread)
            assert(ok and result == "done")
            """
        );
    }

    [Test]
    public async Task PropertyWriteBeforeMethodCall_PreservesTransformReceiver()
    {
        using var state = LuaState.Create();
        state.OpenStandardLibraries();
        await state.DoStringAsync(
            """
            local Vector3 = {}
            Vector3.__index = Vector3
            function Vector3.New(x, y, z)
                return setmetatable({x = x, y = y, z = z}, Vector3)
            end
            function Vector3:Clone() return Vector3.New(self.x, self.y, self.z) end
            function Vector3.Up() return Vector3.New(0, 1, 0) end
            Vector3.__sub = function(a, b)
                return Vector3.New(a.x - b.x, a.y - b.y, a.z - b.z)
            end
            local Transform = {}
            local getters = {position = function(self) return self.localPositionValue:Clone() end}
            local setters = {position = function(self, value) self.localPositionValue = value:Clone() end}
            Transform.__index = function(self, key)
                local getter = getters[key]
                if getter then return getter(self) end
                return Transform[key]
            end
            Transform.__newindex = function(self, key, value)
                local setter = setters[key]
                if setter then setter(self, value) else rawset(self, key, value) end
            end
            function Transform:LookAt(target, up)
                assert(getmetatable(self) == Transform, "LookAt received a Vector3 as self")
                self.rotation = target - self.position
                assert(up.y == 1)
            end
            local transform = setmetatable({localPositionValue = Vector3.New(0, 0, 0)}, Transform)
            for i = 1, 3 do
                transform:LookAt(Vector3.New(0, 0.5, 2), Vector3.Up())
                local position = transform.position
                transform.position = Vector3.New(0, 5, 20)
                transform:LookAt(Vector3.New(0, 0.5, 2), Vector3.Up())
                assert(transform.rotation.y == -4.5 and transform.rotation.z == -18)
            end
            """
        );
    }
}
