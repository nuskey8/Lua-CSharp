using System.Runtime.CompilerServices;

namespace Lua.Tests;

sealed class ManualSuspension : INotifyCompletion
{
    Action? continuation;

    public ManualSuspension GetAwaiter() => this;

    public bool IsCompleted => false;

    public void GetResult() { }

    public void OnCompleted(Action continuation) => this.continuation = continuation;

    // Return from the continuation before callers assert collectibility.
    [MethodImpl(MethodImplOptions.NoInlining)]
    public void Resume()
    {
        var callback = continuation!;
        continuation = null;
        callback();
    }
}
