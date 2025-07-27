#pragma warning disable CS1591
/*

IStateMachineRunnerPromise is  based on UniTask
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
using System.Threading.Tasks.Sources;

// ReSharper disable ArrangeTypeMemberModifiers

namespace Lua.Internal.CompilerServices;

interface IStateMachineRunnerPromise : IValueTaskSource
{
    Action MoveNext { get; }
    ValueTask Task { get; }
    void SetResult();
    void SetException(Exception exception);
}

interface IStateMachineRunnerPromise<T> : IValueTaskSource<T>
{
    Action MoveNext { get; }
    ValueTask<T> Task { get; }
    void SetResult(T result);
    void SetException(Exception exception);
}

sealed class LightAsyncValueTask<TStateMachine> : IStateMachineRunnerPromise, IPoolNode<LightAsyncValueTask<TStateMachine>>
    where TStateMachine : IAsyncStateMachine
{
    static LinkedPool<LightAsyncValueTask<TStateMachine>> pool;

    public Action MoveNext { get; }

    TStateMachine? stateMachine;
    ManualResetValueTaskSourceCore<byte> core;

    LightAsyncValueTask()
    {
        MoveNext = Run;
    }

    public static void SetStateMachine(ref TStateMachine stateMachine, ref IStateMachineRunnerPromise? runnerPromiseFieldRef)
    {
        if (!pool.TryPop(out var result))
        {
            result = new();
        }

        runnerPromiseFieldRef = result; // set runner before copied.
        result.stateMachine = stateMachine; // copy struct StateMachine(in release build).
    }

    LightAsyncValueTask<TStateMachine>? nextNode;

    public ref LightAsyncValueTask<TStateMachine>? NextNode
    {
        get
        {
            return ref nextNode;
        }
    }

    void Return()
    {
        core.Reset();
        stateMachine = default;
        pool.TryPush(this);
    }


    [DebuggerHidden]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    void Run()
    {
        stateMachine!.MoveNext();
    }

    public ValueTask Task
    {
        [DebuggerHidden]
        get
        {
            return new(this, core.Version);
        }
    }

    [DebuggerHidden]
    public void SetResult()
    {
        core.SetResult(0);
    }

    [DebuggerHidden]
    public void SetException(Exception exception)
    {
        core.SetException(exception);
    }

    [DebuggerHidden]
    public void GetResult(short token)
    {
        try
        {
            core.GetResult(token);
        }
        finally
        {
            Return();
        }
    }

    [DebuggerHidden]
    public ValueTaskSourceStatus GetStatus(short token)
    {
        return core.GetStatus(token);
    }


    [DebuggerHidden]
    public void OnCompleted(Action<object?> continuation, object? state, short token, ValueTaskSourceOnCompletedFlags flags)
    {
        core.OnCompleted(continuation, state, token, flags);
    }
}

sealed class LightAsyncValueTask<TStateMachine, T> : IStateMachineRunnerPromise<T>, IPoolNode<LightAsyncValueTask<TStateMachine, T>>
    where TStateMachine : IAsyncStateMachine
{
    static LinkedPool<LightAsyncValueTask<TStateMachine, T>> pool;

    public Action MoveNext { get; }

    TStateMachine? stateMachine;
    ManualResetValueTaskSourceCore<T> core;

    LightAsyncValueTask()
    {
        MoveNext = Run;
    }

    public static void SetStateMachine(ref TStateMachine stateMachine, ref IStateMachineRunnerPromise<T>? runnerPromiseFieldRef)
    {
        if (!pool.TryPop(out var result))
        {
            result = new();
        }

        runnerPromiseFieldRef = result; // set runner before copied.
        result.stateMachine = stateMachine; // copy struct StateMachine(in release build).
    }

    LightAsyncValueTask<TStateMachine, T>? nextNode;

    public ref LightAsyncValueTask<TStateMachine, T>? NextNode
    {
        get
        {
            return ref nextNode;
        }
    }


    void Return()
    {
        core.Reset();
        stateMachine = default!;
        pool.TryPush(this);
    }


    [DebuggerHidden]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    void Run()
    {
        stateMachine!.MoveNext();
    }

    public ValueTask<T> Task
    {
        [DebuggerHidden]
        get
        {
            return new(this, core.Version);
        }
    }

    [DebuggerHidden]
    public void SetResult(T result)
    {
        core.SetResult(result);
    }

    [DebuggerHidden]
    public void SetException(Exception exception)
    {
        core.SetException(exception);
    }

    [DebuggerHidden]
    public T GetResult(short token)
    {
        try
        {
            return core.GetResult(token);
        }
        finally
        {
            Return();
        }
    }

    [DebuggerHidden]
    T IValueTaskSource<T>.GetResult(short token)
    {
        return GetResult(token);
    }

    [DebuggerHidden]
    public ValueTaskSourceStatus GetStatus(short token)
    {
        return core.GetStatus(token);
    }


    [DebuggerHidden]
    public void OnCompleted(Action<object?> continuation, object? state, short token, ValueTaskSourceOnCompletedFlags flags)
    {
        core.OnCompleted(continuation, state, token, flags);
    }
}