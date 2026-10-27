using Lua.Standard;

namespace Lua.Tests;

public class AssignmentConflictTests
{
    static LuaState CreateState()
    {
        var state = LuaState.Create();
        state.OpenStandardLibraries();
        return state;
    }

    [TestCase("local t={}; local i=1; t[i],i='x',2; assert(t[1]=='x' and t[2]==nil and i==2)")]
    [TestCase("local t={}; local old=t; t.x,t='x',{}; assert(old.x=='x' and t.x==nil)")]
    [TestCase(
        "local t={}; local old=t; local function f() t.x,t='x',{} end; f(); assert(old.x=='x' and t.x==nil)"
    )]
    [TestCase(
        "local a,b={},{}; local i=1; a[i],b[i],i='a','b',2; assert(a[1]=='a' and b[1]=='b' and a[2]==nil and b[2]==nil)"
    )]
    [TestCase("local t={}; local i=1; t[i],i='x'; assert(t[1]=='x' and t[2]==nil and i==nil)")]
    [TestCase(
        "local t={}; local i=1; local function f() return 'x',2 end; t[i],i=f(); assert(t[1]=='x' and t[2]==nil and i==2)"
    )]
    [TestCase(
        "local t={}; local old=t; local i=1; t[i],i,t='x',2,{}; assert(old[1]=='x' and old[2]==nil and t[1]==nil)"
    )]
    [TestCase("local t={}; local i=1; t[i],t[i],i='a','b',2; assert(t[1]=='a' and t[2]==nil)")]
    public async Task AssignmentConflictPreservesEveryOriginalTableAndIndex(string source)
    {
        using var state = CreateState();
        var closure = state.Load(source, "assignment-conflict");
        await state.ExecuteAsync(closure);
        await state.ExecuteAsync(
            state.Load(closure.Proto.ToBytecode(), "assignment-conflict-roundtrip")
        );
        Assert.That(state.Stack.Count, Is.Zero);
        Assert.That(state.CallStackFrameCount, Is.Zero);
    }
}
