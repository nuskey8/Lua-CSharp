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
        var assert = state.Environment["assert"].Read<LuaFunction>();
        state.Environment["assert"] = new LuaFunction("assert_with_wait",
            async (context, ct) =>
            {
                await Task.Delay(1, ct);
                var arg0 = context.GetArgument(0);

                if (!arg0.ToBoolean())
                {
                    var message = "assertion failed!";
                    if (context.HasArgument(1))
                    {
                        message = context.GetArgument<string>(1);
                    }

                    throw new LuaAssertionException(context.State, message);
                }

                return context.Return(context.Arguments);
            });
    }

    [Test]
    [TestCase("tests-lua/coroutine.lua")]
    [TestCase("tests-lua/db.lua")]
    [TestCase("tests-lua/vararg.lua")]
    public async Task Test_Async(string file)
    {
        var path = FileHelper.GetAbsolutePath(file);
        try
        {
            await state.DoFileAsync(path);
        }
        catch (LuaRuntimeException e)
        {
            var line = e.LuaTraceback!.LastLine;
            throw new($"{path}:line {line}\n{e.InnerException}\n {e}");
        }
    }
}