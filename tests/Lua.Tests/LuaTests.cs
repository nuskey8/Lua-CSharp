using Lua.Standard;
using System.Globalization;

namespace Lua.Tests;

public class LuaTests
{
    [Test]
    [Parallelizable(ParallelScope.All)]
    [TestCase("tests-lua/code.lua")]
    [TestCase("tests-lua/goto.lua")]
    [TestCase("tests-lua/constructs.lua")]
    [TestCase("tests-lua/locals.lua")]
    [TestCase("tests-lua/literals.lua")]
    //[TestCase("tests-lua/pm.lua")] //string.match is not implemented
    //[TestCase("tests-lua/sort.lua")] //check for "invalid order function" is not implemented
    //[TestCase("tests-lua/calls.lua")] //  string.dump and reader function for load chunk is not implemented
    [TestCase("tests-lua/closure.lua")]
    //[TestCase("tests-lua/errors.lua")] // get table name  if nil is not implemented
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
        var state = LuaState.Create();
        state.OpenStandardLibraries();
        var path = FileHelper.GetAbsolutePath(file);
        Directory.SetCurrentDirectory(Path.GetDirectoryName(path)!);
        try
        {
            await state.DoFileAsync(Path.GetFileName(file));
        }
        catch (LuaRuntimeException e)
        {
            var luaTraceback = e.LuaTraceback;
            if (luaTraceback == null)
            {
                throw;
            }

            var line = luaTraceback.LastLine;
            throw new Exception($"{path}:line {line}\n{e.InnerException}\n {e}");
        }
    }
}