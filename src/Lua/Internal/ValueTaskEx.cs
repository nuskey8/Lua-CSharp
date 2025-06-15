using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Threading.Tasks.Sources;

/*

ValueTaskEx based on ValueTaskSupprement but modified for pooling
https://github.com/Cysharp/ValueTaskSupplement

MIT License

Copyright (c) 2019 Cysharp, Inc.

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.

*/

namespace Lua.Internal;

internal static class ContinuationSentinel
{
    public static readonly Action<object?> AvailableContinuation = _ => { };
    public static readonly Action<object?> CompletedContinuation = _ => { };
}

internal static class ValueTaskEx
{
    public static ValueTask<(int winArgumentIndex, T0 result0, T1 result1, IDisposable promis)> WhenAnyPooled<T0, T1>(ValueTask<T0> task0, ValueTask<T1> task1)
    {
        var promise = WhenAnyPromise<T0, T1>.Get(task0, task1);
        return new(promise, 0);
    }

    class WhenAnyPromise<T0, T1> : IValueTaskSource<(int winArgumentIndex, T0 result0, T1 result1, IDisposable)>, IPoolNode<WhenAnyPromise<T0, T1>>, IDisposable
    {
        static readonly ContextCallback execContextCallback = ExecutionContextCallback!;
        static readonly SendOrPostCallback syncContextCallback = SynchronizationContextCallback!;

        static LinkedPool<WhenAnyPromise<T0, T1>> pool;
        WhenAnyPromise<T0, T1>? nextNode;
        public ref WhenAnyPromise<T0, T1>? NextNode => ref nextNode;

        public static WhenAnyPromise<T0, T1> Get(ValueTask<T0> task0, ValueTask<T1> task1)
        {
            if (pool.TryPop(out var f))
            {
                f.Init(task0, task1);
            }
            else
            {
                f = new WhenAnyPromise<T0, T1>(task0, task1);
            }

            return f;
        }

        public void Dispose()
        {
            t0 = default!;
            t1 = default!;
            awaiter0 = default!;
            awaiter1 = default!;

            completedCount = default!;
            winArgumentIndex = -1;
            exception = default!;
            continuation = ContinuationSentinel.AvailableContinuation;
            invokeContinuation = default!;
            state = default!;
            syncContext = default!;
            execContext = default!;
            pool.TryPush(this);
        }

        T0 t0 = default!;
        T1 t1 = default!;
        ValueTaskAwaiter<T0> awaiter0;
        ValueTaskAwaiter<T1> awaiter1;

        int completedCount = 0;
        int winArgumentIndex = -1;
        ExceptionDispatchInfo? exception;
        Action<object?> continuation = ContinuationSentinel.AvailableContinuation;
        Action<object?>? invokeContinuation;
        object? state;
        SynchronizationContext? syncContext;
        ExecutionContext? execContext;

        public void Init(ValueTask<T0> task0, ValueTask<T1> task1)
        {
            {
                var awaiter = task0.GetAwaiter();
                if (awaiter.IsCompleted)
                {
                    try
                    {
                        t0 = awaiter.GetResult();
                        TryInvokeContinuationWithIncrement(0);
                        return;
                    }
                    catch (Exception ex)
                    {
                        exception = ExceptionDispatchInfo.Capture(ex);
                        return;
                    }
                }
                else
                {
                    awaiter0 = awaiter;
                    awaiter.UnsafeOnCompleted(ContinuationT0);
                }
            }
            {
                var awaiter = task1.GetAwaiter();
                if (awaiter.IsCompleted)
                {
                    try
                    {
                        t1 = awaiter.GetResult();
                        TryInvokeContinuationWithIncrement(1);
                        return;
                    }
                    catch (Exception ex)
                    {
                        exception = ExceptionDispatchInfo.Capture(ex);
                        return;
                    }
                }
                else
                {
                    awaiter1 = awaiter;
                    awaiter.UnsafeOnCompleted(ContinuationT1);
                }
            }
        }

        public WhenAnyPromise(ValueTask<T0> task0, ValueTask<T1> task1)
        {
            {
                var awaiter = task0.GetAwaiter();
                if (awaiter.IsCompleted)
                {
                    try
                    {
                        t0 = awaiter.GetResult();
                        TryInvokeContinuationWithIncrement(0);
                        return;
                    }
                    catch (Exception ex)
                    {
                        exception = ExceptionDispatchInfo.Capture(ex);
                        return;
                    }
                }
                else
                {
                    awaiter0 = awaiter;
                    awaiter.UnsafeOnCompleted(ContinuationT0);
                }
            }
            {
                var awaiter = task1.GetAwaiter();
                if (awaiter.IsCompleted)
                {
                    try
                    {
                        t1 = awaiter.GetResult();
                        TryInvokeContinuationWithIncrement(1);
                        return;
                    }
                    catch (Exception ex)
                    {
                        exception = ExceptionDispatchInfo.Capture(ex);
                        return;
                    }
                }
                else
                {
                    awaiter1 = awaiter;
                    awaiter.UnsafeOnCompleted(ContinuationT1);
                }
            }
        }

        void ContinuationT0()
        {
            try
            {
                t0 = awaiter0.GetResult();
            }
            catch (Exception ex)
            {
                exception = ExceptionDispatchInfo.Capture(ex);
                TryInvokeContinuation();
                return;
            }

            TryInvokeContinuationWithIncrement(0);
        }

        void ContinuationT1()
        {
            try
            {
                t1 = awaiter1.GetResult();
            }
            catch (Exception ex)
            {
                exception = ExceptionDispatchInfo.Capture(ex);
                TryInvokeContinuation();
                return;
            }

            TryInvokeContinuationWithIncrement(1);
        }


        void TryInvokeContinuationWithIncrement(int index)
        {
            if (Interlocked.Increment(ref completedCount) == 1)
            {
                Volatile.Write(ref winArgumentIndex, index);
                TryInvokeContinuation();
            }
        }

        void TryInvokeContinuation()
        {
            var c = Interlocked.Exchange(ref continuation, ContinuationSentinel.CompletedContinuation);
            if (c != ContinuationSentinel.AvailableContinuation && c != ContinuationSentinel.CompletedContinuation)
            {
                var spinWait = new SpinWait();
                while (state == null) // worst case, state is not set yet so wait.
                {
                    spinWait.SpinOnce();
                }

                if (execContext != null)
                {
                    invokeContinuation = c;
                    ExecutionContext.Run(execContext, execContextCallback, this);
                }
                else if (syncContext != null)
                {
                    invokeContinuation = c;
                    syncContext.Post(syncContextCallback, this);
                }
                else
                {
                    c(state);
                }
            }
        }

        public (int winArgumentIndex, T0 result0, T1 result1, IDisposable) GetResult(short token)
        {
            if (exception != null)
            {
                exception.Throw();
            }

            var i = winArgumentIndex;
            return (winArgumentIndex, t0, t1, this);
        }

        public ValueTaskSourceStatus GetStatus(short token)
        {
            return Volatile.Read(ref winArgumentIndex) != -1 ? ValueTaskSourceStatus.Succeeded
                : exception != null ? exception.SourceException is OperationCanceledException ? ValueTaskSourceStatus.Canceled : ValueTaskSourceStatus.Faulted
                : ValueTaskSourceStatus.Pending;
        }

        public void OnCompleted(Action<object?> continuation, object? state, short token, ValueTaskSourceOnCompletedFlags flags)
        {
            var c = Interlocked.CompareExchange(ref this.continuation, continuation, ContinuationSentinel.AvailableContinuation);
            if (c == ContinuationSentinel.CompletedContinuation)
            {
                continuation(state);
                return;
            }

            if (c != ContinuationSentinel.AvailableContinuation)
            {
                throw new InvalidOperationException("does not allow multiple await.");
            }

            if (state == null)
            {
                throw new InvalidOperationException("invalid state.");
            }

            if ((flags & ValueTaskSourceOnCompletedFlags.FlowExecutionContext) == ValueTaskSourceOnCompletedFlags.FlowExecutionContext)
            {
                execContext = ExecutionContext.Capture();
            }

            if ((flags & ValueTaskSourceOnCompletedFlags.UseSchedulingContext) == ValueTaskSourceOnCompletedFlags.UseSchedulingContext)
            {
                syncContext = SynchronizationContext.Current;
            }

            this.state = state;

            if (GetStatus(token) != ValueTaskSourceStatus.Pending)
            {
                TryInvokeContinuation();
            }
        }

        static void ExecutionContextCallback(object state)
        {
            var self = (WhenAnyPromise<T0, T1>)state;
            if (self.syncContext != null)
            {
                self.syncContext.Post(syncContextCallback, self);
            }
            else
            {
                var invokeContinuation = self.invokeContinuation!;
                var invokeState = self.state;
                self.invokeContinuation = null;
                self.state = null;
                invokeContinuation(invokeState);
            }
        }

        static void SynchronizationContextCallback(object state)
        {
            var self = (WhenAnyPromise<T0, T1>)state;
            var invokeContinuation = self.invokeContinuation!;
            var invokeState = self.state;
            self.invokeContinuation = null;
            self.state = null;
            invokeContinuation(invokeState);
        }
    }
}