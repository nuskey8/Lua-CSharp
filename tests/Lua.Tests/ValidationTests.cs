using Lua.Runtime;
using Lua.Standard;

namespace Lua.Tests;

public class ValidationTests
{
    [Test]
    public async Task Test_Simple()
    {
       var state = LuaState.Create();
        state.OpenStandardLibraries();
        LuaThreadAccess innerAccess = default!;
        state.Environment["wait"] = new LuaFunction("wait",
            async (context, ct) =>
            {
                innerAccess = context.Access;
                await Task.Delay((int)(context.GetArgument<double>(0) * 1000), ct);
                return context.Return(context.Arguments);
            });
         
        var task=state.DoStringAsync("wait(0.5)");
         
        await Task.Delay(100);
        Assert.That(task.IsCompleted, Is.False);
        Assert.ThrowsAsync<InvalidOperationException>( async () =>
        {
            await state.DoStringAsync("print('hello')");
        });
        await task;
        
        Assert.ThrowsAsync<InvalidOperationException>( async () =>
        {
            await innerAccess.DoStringAsync("print('hello')");
        });
        Assert.DoesNotThrowAsync(async () =>
        {
            await state.DoStringAsync("wait(0.5)");
        });
    }
    
    [Test]
    public async Task Test_Recursive()
    {
        var state = LuaState.Create();
        state.OpenStandardLibraries();
        state.Environment["dostring"] = new LuaFunction("dostring",
            async (context, ct) => context.Return(await context.Access.DoStringAsync(context.GetArgument<string>(0), null, ct)));
         
        var result=await state.DoStringAsync("""return dostring("return 1")""");
         
        Assert.That(result.Length, Is.EqualTo(1));
        Assert.That(result[0].Read<double>(), Is.EqualTo(1));
    }
    
}