using Lua.Standard;
using System.Globalization;

namespace Lua.Tests;

public class LuaTests
{
    LuaState state = default!;

    [SetUp]
    public void SetUp()
    {
        state = LuaState.Create();
        state.OpenStandardLibraries();
    }
    

    [Test]
    [TestCase("tests-lua/code.lua")]
    [TestCase("tests-lua/goto.lua")]
    [TestCase("tests-lua/constructs.lua")]
    [TestCase("tests-lua/locals.lua")]
    [TestCase("tests-lua/literals.lua")]
    //[TestCase("tests-lua/pm.lua")] string.match is not implemented
    //[TestCase("tests-lua/sort.lua")] //check for "invalid order function" is not implemented
    //[TestCase("tests-lua/calls.lua")] //  string.dump and reader function for load chunk is not implemented
    [TestCase("tests-lua/closure.lua")]
    [TestCase("tests-lua/events.lua")]
    [TestCase("tests-lua/vararg.lua")]
    [TestCase("tests-lua/nextvar.lua")]
    [TestCase("tests-lua/math.lua")]
    [TestCase("tests-lua/bitwise.lua")]
    [TestCase("tests-lua/strings.lua")]
    [TestCase("tests-lua/coroutine.lua")]
    [TestCase("tests-lua/db.lua")]
    [TestCase("tests-lua/verybig.lua")]
    
    public async Task Test_Lua(string file)
    {
        var path = FileHelper.GetAbsolutePath(file);
        try
        {
            await state.DoFileAsync(FileHelper.GetAbsolutePath(file));
        }
        catch (LuaRuntimeException e)
        {
            var traceback = e.LuaTraceback;
            var line = traceback.LastLine;
            throw new Exception($"{path}:line {line}\n{e.InnerException}\n{e} ");
        }
    }
}