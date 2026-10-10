using Lua.Standard;

namespace Lua.Tests;

/// <summary>
/// Reproduces https://github.com/nuskey8/Lua-CSharp/issues/327 — coroutine.yield must not
/// schedule continuations onto SynchronizationContext, or single-threaded hosts (Unity WebGL)
/// deadlock when ExecuteAsync is driven via GetAwaiter().GetResult().
/// </summary>
public class CoroutineSyncContextTests
{
    [Test]
    public void ExecuteAsync_CoroutineYield_CompletesSynchronously_UnderSyncContext()
    {
        var previous = SynchronizationContext.Current;
        var syncContext = new NonPumpingSynchronizationContext();
        SynchronizationContext.SetSynchronizationContext(syncContext);
        try
        {
            var state = LuaState.Create();
            state.OpenBasicLibrary();
            state.OpenCoroutineLibrary();

            var closure = state.Load(
                """
                local co = coroutine.create(function(a)
                    local b = coroutine.yield(a + 1)
                    return b * 2
                end)
                local _, x = coroutine.resume(co, 10)
                local _, y = coroutine.resume(co, 5)
                return x + y
                """,
                "@coroutine_sync.lua"
            );

            var task = state.ExecuteAsync(closure);

            Assert.That(
                task.IsCompleted,
                Is.True,
                "ExecuteAsync suspended and posted a continuation to SynchronizationContext; "
                    + "this deadlocks on single-threaded runtimes when GetResult() blocks the only thread."
            );
            Assert.That(syncContext.PostCount, Is.EqualTo(0));

            var results = task.GetAwaiter().GetResult();
            Assert.That(results, Has.Length.EqualTo(1));
            Assert.That(results[0].Read<double>(), Is.EqualTo(21));
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }
    }

    [Test]
    public void ExecuteAsync_PureCompute_CompletesSynchronously_UnderSyncContext()
    {
        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new NonPumpingSynchronizationContext());
        try
        {
            var state = LuaState.Create();
            state.OpenBasicLibrary();

            var task = state.ExecuteAsync(state.Load("return (2 + 3) * 4", "@pure.lua"));

            Assert.That(task.IsCompleted, Is.True);
            var results = task.GetAwaiter().GetResult();
            Assert.That(results[0].Read<double>(), Is.EqualTo(20));
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }
    }

    /// <summary>
    /// Queues Post callbacks but never pumps them — same failure mode as Unity WebGL when the
    /// main thread is blocked in GetAwaiter().GetResult().
    /// </summary>
    sealed class NonPumpingSynchronizationContext : SynchronizationContext
    {
        public int PostCount { get; private set; }

        public override void Post(SendOrPostCallback d, object? state)
        {
            PostCount++;
        }

        public override void Send(SendOrPostCallback d, object? state)
        {
            d(state);
        }
    }
}
