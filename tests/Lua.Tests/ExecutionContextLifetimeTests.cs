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
        // A completed async continuation may still be unwinding on another thread.
        Assert.That(
            () =>
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
                return reference.IsAlive;
            },
            Is.False.After(1000, 10),
            "A pooled VM context retained its Lua state."
        );
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static WeakReference RunAndDispose(bool asynchronous, bool failure)
    {
        using var state = LuaState.Create();
        state.Environment["callback"] = new LuaFunction(
            async (context, _) =>
            {
                if (asynchronous)
                    await Task.Yield();
                if (failure)
                    throw new LuaRuntimeException(context.State, "failure");
                return context.Return(42);
            }
        );
        try
        {
            state.DoStringAsync("return callback()").AsTask().GetAwaiter().GetResult();
            Assert.That(failure, Is.False);
        }
        catch (LuaRuntimeException) when (failure) { }
        return new WeakReference(state);
    }
}
