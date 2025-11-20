#pragma warning disable CS1591
/*

LightAsyncValueTaskMethodBuilder is  based on UniTask
https://github.com/Cysharp/UniTask

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
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security;

namespace Lua.Internal.CompilerServices;

[StructLayout(LayoutKind.Auto)]
struct LightAsyncValueTaskMethodBuilder
{
    IStateMachineRunnerPromise? runnerPromise;
    Exception? ex;

    // 1. Static Create method.
    [DebuggerHidden]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static LightAsyncValueTaskMethodBuilder Create()
    {
        return default;
    }

    // 2. TaskLike Task property.
    public ValueTask Task
    {
        [DebuggerHidden]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            if (runnerPromise != null)
            {
                return runnerPromise.Task;
            }

            if (ex != null)
            {
                return new(System.Threading.Tasks.Task.FromException(ex));
            }

            return new(System.Threading.Tasks.Task.CompletedTask);
        }
    }

    // 3. SetException
    [DebuggerHidden]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetException(Exception exception)
    {
        if (runnerPromise == null)
        {
            ex = exception;
        }
        else
        {
            runnerPromise.SetException(exception);
        }
    }

    // 4. SetResult
    [DebuggerHidden]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetResult()
    {
        runnerPromise?.SetResult();
    }

    // 5. AwaitOnCompleted
    [DebuggerHidden]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void AwaitOnCompleted<TAwaiter, TStateMachine>(ref TAwaiter awaiter, ref TStateMachine stateMachine)
        where TAwaiter : INotifyCompletion
        where TStateMachine : IAsyncStateMachine
    {
        if (runnerPromise == null)
        {
            LightAsyncValueTask<TStateMachine>.SetStateMachine(ref stateMachine, ref runnerPromise);
        }

        awaiter.OnCompleted(runnerPromise!.MoveNext);
    }

    // 6. AwaitUnsafeOnCompleted
    [DebuggerHidden]
    [SecuritySafeCritical]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void AwaitUnsafeOnCompleted<TAwaiter, TStateMachine>(ref TAwaiter awaiter, ref TStateMachine stateMachine)
        where TAwaiter : ICriticalNotifyCompletion
        where TStateMachine : IAsyncStateMachine
    {
        if (runnerPromise == null)
        {
            LightAsyncValueTask<TStateMachine>.SetStateMachine(ref stateMachine, ref runnerPromise);
        }

        awaiter.UnsafeOnCompleted(runnerPromise!.MoveNext);
    }

    // 7. Start
    [DebuggerHidden]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Start<TStateMachine>(ref TStateMachine stateMachine)
        where TStateMachine : IAsyncStateMachine
    {
        stateMachine.MoveNext();
    }

    // 8. SetStateMachine
    [DebuggerHidden]
    public void SetStateMachine(IAsyncStateMachine stateMachine)
    {
        // don't use boxed stateMachine.
    }
}

[StructLayout(LayoutKind.Auto)]
struct LightAsyncValueTaskMethodBuilder<T>
{
    IStateMachineRunnerPromise<T>? runnerPromise;
    Exception? ex;
    T result;

    // 1. Static Create method.
    [DebuggerHidden]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static LightAsyncValueTaskMethodBuilder<T> Create()
    {
        return default;
    }

    // 2. TaskLike Task property.
    public ValueTask<T> Task
    {
        [DebuggerHidden]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            if (runnerPromise != null)
            {
                return runnerPromise.Task;
            }

            if (ex != null)
            {
                return new(System.Threading.Tasks.Task.FromException<T>(ex));
            }

            {
                return new(System.Threading.Tasks.Task.FromResult(result));
            }
        }
    }

    // 3. SetException
    [DebuggerHidden]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetException(Exception exception)
    {
        if (runnerPromise == null)
        {
            ex = exception;
        }
        else
        {
            runnerPromise.SetException(exception);
        }
    }

    // 4. SetResult
    [DebuggerHidden]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetResult(T result)
    {
        if (runnerPromise == null)
        {
            this.result = result;
        }
        else
        {
            runnerPromise.SetResult(result);
        }
    }

    // 5. AwaitOnCompleted
    [DebuggerHidden]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void AwaitOnCompleted<TAwaiter, TStateMachine>(ref TAwaiter awaiter, ref TStateMachine stateMachine)
        where TAwaiter : INotifyCompletion
        where TStateMachine : IAsyncStateMachine
    {
        if (runnerPromise == null)
        {
            LightAsyncValueTask<TStateMachine, T>.SetStateMachine(ref stateMachine, ref runnerPromise);
        }

        awaiter.OnCompleted(runnerPromise!.MoveNext);
    }

    // 6. AwaitUnsafeOnCompleted
    [DebuggerHidden]
    [SecuritySafeCritical]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void AwaitUnsafeOnCompleted<TAwaiter, TStateMachine>(ref TAwaiter awaiter, ref TStateMachine stateMachine)
        where TAwaiter : ICriticalNotifyCompletion
        where TStateMachine : IAsyncStateMachine
    {
        if (runnerPromise == null)
        {
            LightAsyncValueTask<TStateMachine, T>.SetStateMachine(ref stateMachine, ref runnerPromise);
        }

        awaiter.UnsafeOnCompleted(runnerPromise!.MoveNext);
    }

    // 7. Start
    [DebuggerHidden]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Start<TStateMachine>(ref TStateMachine stateMachine)
        where TStateMachine : IAsyncStateMachine
    {
        stateMachine.MoveNext();
    }

    // 8. SetStateMachine
    [DebuggerHidden]
    public void SetStateMachine(IAsyncStateMachine stateMachine)
    {
        // don't use boxed stateMachine.
    }
}