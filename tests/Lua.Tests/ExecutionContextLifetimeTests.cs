using System.Runtime.CompilerServices;

namespace Lua.Tests;

public class ExecutionContextLifetimeTests
{
    [TestCase(false, false)]
    [TestCase(true, false)]
    [TestCase(false, true)]
    [TestCase(true, true)]
    public void CompletedExecutionDoesNotRootDisposedState(bool asynchronous, bool failure)
    {
        var reference = RunAndDispose(asynchronous, failure);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        Assert.That(reference.IsAlive, Is.False, "A pooled VM context retained its Lua state.");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static WeakReference RunAndDispose(bool asynchronous, bool failure)
    {
        using var state = LuaState.Create();
        var suspension = new ManualSuspension();
        state.Environment["callback"] = new LuaFunction(
            async (context, _) =>
            {
                if (asynchronous)
                    await suspension;
                if (failure)
                    throw new LuaRuntimeException(context.State, "failure");
                return context.Return(42);
            }
        );
        try
        {
            var operation = state.DoStringAsync("return callback()").AsTask();
            if (asynchronous)
                suspension.Resume();
            operation.GetAwaiter().GetResult();
            Assert.That(failure, Is.False);
        }
        catch (LuaRuntimeException) when (failure) { }
        return new WeakReference(state);
    }
}
