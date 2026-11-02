using System.Collections.Concurrent;
using System.Runtime.ExceptionServices;

namespace Lua.Debugging;

sealed class SynchronousDebugPause(SynchronizationContext? previous) : SynchronizationContext
{
    readonly BlockingCollection<(SendOrPostCallback Callback, object? State)> work = new();
    readonly object gate = new();
    readonly int threadId = Environment.CurrentManagedThreadId;
    bool accepting = true;

    public static int Run(
        Func<CancellationToken, ValueTask<int>> action,
        CancellationToken cancellationToken
    )
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var previous = Current;
        var context = new SynchronousDebugPause(previous);
        SetSynchronizationContext(context);
        try
        {
            var operation = action(cancellation.Token);
            if (operation.IsCompleted)
            {
                context.Complete();
                if (context.work.Count == 0)
                    return operation.GetAwaiter().GetResult();
            }
            var task = operation.AsTask();
            _ = task.ContinueWith(
                static (_, state) => ((SynchronousDebugPause)state!).Complete(),
                context,
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default
            );
            ExceptionDispatchInfo? failure = null;
            foreach (var item in context.work.GetConsumingEnumerable())
                try
                {
                    item.Callback(item.State);
                }
                catch (Exception exception)
                {
                    failure ??= ExceptionDispatchInfo.Capture(exception);
                    try
                    {
                        cancellation.Cancel();
                    }
                    catch (AggregateException)
                    {
                        // Drain the suspended operation before propagating the original failure.
                    }
                }
            if (failure != null)
            {
                _ = task.Exception;
                failure.Throw();
            }
            return task.GetAwaiter().GetResult();
        }
        finally
        {
            context.Complete();
            SetSynchronizationContext(previous);
            context.work.Dispose();
        }
    }

    public override SynchronizationContext CreateCopy() => this;

    public override void Post(SendOrPostCallback callback, object? state)
    {
        lock (gate)
        {
            if (accepting)
            {
                work.Add((callback, state));
                return;
            }
        }
        if (previous != null)
            previous.Post(callback, state);
        else
            ThreadPool.QueueUserWorkItem(_ => callback(state));
    }

    public override void Send(SendOrPostCallback callback, object? state)
    {
        if (Environment.CurrentManagedThreadId == threadId)
        {
            callback(state);
            return;
        }
        var completed = new TaskCompletionSource<bool>();
        Post(
            _ =>
            {
                try
                {
                    callback(state);
                    completed.SetResult(true);
                }
                catch (Exception exception)
                {
                    completed.SetException(exception);
                }
            },
            null
        );
        completed.Task.GetAwaiter().GetResult();
    }

    void Complete()
    {
        lock (gate)
        {
            if (!accepting)
                return;
            accepting = false;
            work.CompleteAdding();
        }
    }
}
