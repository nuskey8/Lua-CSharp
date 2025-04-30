using Lua.Standard;

namespace Lua.Tests;

public class AsyncTests
{
    LuaState state = default!;

    [SetUp]
    public void SetUp()
    {
        state = LuaState.Create();
        state.OpenStandardLibraries();
        var assert = state.Environment["assert"].Read<LuaFunction>() ;
        state.Environment["assert"] = new LuaFunction("wait",
            async (c, ct) =>
            {
                await Task.Delay(1, ct);
                return await assert.InvokeAsync(c, ct);
            });
    }
    
    [Test]
    public  async Task Test_Async()
    {
        var path = FileHelper.GetAbsolutePath("tests-lua/coroutine.lua");
        try
        {
            await state.DoFileAsync(path);
        }
        catch (LuaRuntimeException e)
        {
            var line = e.LuaTraceback.LastLine;
            throw new Exception($"{path}:line {line}\n{e.InnerException}\n {e}");
        }
    }
}