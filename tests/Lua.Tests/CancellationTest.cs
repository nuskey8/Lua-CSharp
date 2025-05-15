using Lua.Standard;

namespace Lua.Tests;

public class CancellationTest
{
    LuaState state = default!;

    [SetUp]
    public void SetUp()
    {
        state = LuaState.Create();
        state.OpenStandardLibraries();

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

                    throw new LuaAssertionException(context.Thread, message);
                }

                return (context.Return(context.Arguments));
            });
        state.Environment["sleep"] = new LuaFunction("sleep",
            (context, _) =>
            {
                Thread.Sleep(context.GetArgument<int>(0));

                return new(context.Return());
            });
        state.Environment["wait"] = new LuaFunction("wait",
            async (context, ct) =>
            {
                await Task.Delay(context.GetArgument<int>(0), ct);
                return context.Return();
            });
    }

    [Test]
    public async Task PCall_WaitTest()
    {
        var source = """
                     local function f(millisec)
                         wait(millisec)
                     end                     
                     pcall(f, 500)
                     """;
        var cancellationTokenSource = new CancellationTokenSource();
        cancellationTokenSource.CancelAfter(200);

        try
        {
            await state.DoStringAsync(source, "@test.lua", cancellationTokenSource.Token);
            Assert.Fail("Expected TaskCanceledException was not thrown.");
        }
        catch (Exception e)
        {
            Assert.That(e, Is.TypeOf<LuaCanceledException>());
            var luaCancelledException = (LuaCanceledException)e;
            Assert.That(luaCancelledException.InnerException, Is.TypeOf<TaskCanceledException>());
            var luaStackTrace = luaCancelledException.LuaTraceback!.ToString();
            Console.WriteLine(luaStackTrace);
            Assert.That(luaStackTrace, Contains.Substring("'wait'"));
            Assert.That(luaStackTrace, Contains.Substring("'pcall'"));
        }
    }

    [Test]
    public async Task PCall_SleepTest()
    {
        var source = """
                     local function f(millisec)
                         sleep(millisec)
                     end                     
                     pcall(f, 500)
                     """;
        var cancellationTokenSource = new CancellationTokenSource();
        cancellationTokenSource.CancelAfter(250);

        try
        {
            await state.DoStringAsync(source, "@test.lua", cancellationTokenSource.Token);
            Assert.Fail("Expected TaskCanceledException was not thrown.");
        }
        catch (Exception e)
        {
            Assert.That(e, Is.TypeOf<LuaCanceledException>());
            var luaCancelledException = (LuaCanceledException)e;
            Assert.That(luaCancelledException.InnerException, Is.Null);
            var luaStackTrace = luaCancelledException.LuaTraceback!.ToString();
            Console.WriteLine(luaStackTrace);
            Assert.That(luaStackTrace, Contains.Substring("'sleep'"));
            Assert.That(luaStackTrace, Contains.Substring("'pcall'"));
        }
    }

    [Test]
    public async Task ForLoopTest()
    {
        var source = """
                     local ret = 0
                     for i = 1, 1000000000 do
                         ret = ret + i
                     end
                     return ret
                     """;
        var cancellationTokenSource = new CancellationTokenSource();
        cancellationTokenSource.CancelAfter(100);
        cancellationTokenSource.Token.Register(() =>
        {
            Console.WriteLine("Cancellation requested");
        });
        try
        {
            var r = await state.DoStringAsync(source, "@test.lua", cancellationTokenSource.Token);
            Console.WriteLine(r[0]);
            Assert.Fail("Expected TaskCanceledException was not thrown.");
        }
        catch (Exception e)
        {
            Assert.That(e, Is.TypeOf<LuaCanceledException>());
            Console.WriteLine(e.StackTrace);
            var luaCancelledException = (LuaCanceledException)e;
            Assert.That(luaCancelledException.InnerException, Is.Null);
            var traceback = luaCancelledException.LuaTraceback;
            if (traceback != null)
            {
                var luaStackTrace = traceback.ToString();
                Console.WriteLine(luaStackTrace);
            }
        }
    }
    
    [Test]
    public async Task GoToLoopTest()
    {
        var source = """
                     local ret = 0
                     ::loop::
                     ret = ret + 1
                     goto loop
                     return ret
                     """;
        var cancellationTokenSource = new CancellationTokenSource();
        cancellationTokenSource.CancelAfter(100);
        cancellationTokenSource.Token.Register(() =>
        {
            Console.WriteLine("Cancellation requested");
        });
        try
        {
            var r = await state.DoStringAsync(source, "@test.lua", cancellationTokenSource.Token);
            Console.WriteLine(r[0]);
            Assert.Fail("Expected TaskCanceledException was not thrown.");
        }
        catch (Exception e)
        {
            Assert.That(e, Is.TypeOf<LuaCanceledException>());
            Console.WriteLine(e.StackTrace);
            var luaCancelledException = (LuaCanceledException)e;
            Assert.That(luaCancelledException.InnerException, Is.Null);
            var traceback = luaCancelledException.LuaTraceback;
            if (traceback != null)
            {
                var luaStackTrace = traceback.ToString();
                Console.WriteLine(luaStackTrace);
            }
        }
    }
}