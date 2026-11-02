using System.Collections.Concurrent;

// A console stand-in for a UI dispatcher: all application callbacks share one thread.
sealed class HostLoop : SynchronizationContext
{
    readonly BlockingCollection<(SendOrPostCallback Callback, object? State)> work = new();

    public static void Run(Func<Task> action)
    {
        var previous = Current;
        var context = new HostLoop();
        SetSynchronizationContext(context);
        try
        {
            var task = action();
            _ = task.ContinueWith(
                _ => context.work.CompleteAdding(),
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default
            );
            foreach (var item in context.work.GetConsumingEnumerable())
                item.Callback(item.State);
            task.GetAwaiter().GetResult();
        }
        finally
        {
            SetSynchronizationContext(previous);
            context.work.Dispose();
        }
    }

    public override void Post(SendOrPostCallback callback, object? state) =>
        work.Add((callback, state));
}
