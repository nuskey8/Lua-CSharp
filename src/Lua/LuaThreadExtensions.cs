using Lua.Runtime;
using System.Runtime.CompilerServices;

namespace Lua;

public static class LuaThreadExtensions
{
    public static UseThreadLease RentUseThread(this LuaThread thread)
    {
        return new(LuaUserThread.Create(thread));
    }

    public static CoroutineLease RentCoroutine(this LuaThread thread, LuaFunction function, bool isProtectedMode = false)
    {
        return new(LuaCoroutine.Create(thread, function, isProtectedMode));
    }


    public static async ValueTask<int> DoStringAsync(this LuaThread thread, string source, Memory<LuaValue> buffer, string? chunkName = null, CancellationToken cancellationToken = default)
    {
        var closure = thread.State.Load(source, chunkName ?? source);
        var count = await thread.RunAsync(closure, cancellationToken);
        using var results = thread.ReadReturnValues(count);
        results.AsSpan()[..Math.Min(buffer.Length, count)].CopyTo(buffer.Span);
        return count;
    }

    public static async ValueTask<LuaValue[]> DoStringAsync(this LuaThread thread, string source, string? chunkName = null, CancellationToken cancellationToken = default)
    {
        var closure = thread.State.Load(source, chunkName ?? source);
        var count = await thread.RunAsync(closure, cancellationToken);
        using var results = thread.ReadReturnValues(count);
        return results.AsSpan().ToArray();
    }

    public static async ValueTask<int> DoFileAsync(this LuaThread thread, string path, Memory<LuaValue> buffer, CancellationToken cancellationToken = default)
    {
        var bytes = await File.ReadAllBytesAsync(path, cancellationToken);
        var fileName = "@" + Path.GetFileName(path);
        var closure = thread.State.Load(bytes, fileName);
        var count = await thread.RunAsync(closure, cancellationToken);
        using var results = thread.ReadReturnValues(count);
        results.AsSpan()[..Math.Min(buffer.Length, results.Length)].CopyTo(buffer.Span);
        return results.Count;
    }

    public static async ValueTask<LuaValue[]> DoFileAsync(this LuaThread thread, string path, CancellationToken cancellationToken = default)
    {
        var bytes = await File.ReadAllBytesAsync(path, cancellationToken);
        var fileName = "@" + Path.GetFileName(path);
        var closure = thread.State.Load(bytes, fileName);
        var count = await thread.RunAsync(closure, cancellationToken);
        using var results = thread.ReadReturnValues(count);
        return results.AsSpan().ToArray();
    }


    public static void Push(this LuaThread thread, LuaValue value)
    {
        thread.CoreData!.Stack.Push(value);
    }

    public static void Push(this LuaThread thread, params ReadOnlySpan<LuaValue> span)
    {
        thread.CoreData!.Stack.PushRange(span);
    }

    public static void Pop(this LuaThread thread, int count)
    {
        thread.CoreData!.Stack.Pop(count);
    }

    public static LuaValue Pop(this LuaThread thread)
    {
        return thread.CoreData!.Stack.Pop();
    }

    public static LuaReturnValuesReader ReadReturnValues(this LuaThread thread, int argumentCount)
    {
        var stack = thread.CoreData!.Stack;
        return new LuaReturnValuesReader(stack, stack.Count - argumentCount);
    }


    public static ref readonly CallStackFrame GetCurrentFrame(this LuaThread thread)
    {
        return ref thread.CoreData!.CallStack.PeekRef();
    }

    public static ReadOnlySpan<LuaValue> GetStackValues(this LuaThread thread)
    {
        if (thread.CoreData == null) return default;
        return thread.CoreData!.Stack.AsSpan();
    }

    public static ReadOnlySpan<CallStackFrame> GetCallStackFrames(this LuaThread thread)
    {
        if (thread.CoreData == null) return default;
        return thread.CoreData!.CallStack.AsSpan();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static void PushCallStackFrame(this LuaThread thread, in CallStackFrame frame)
    {
        thread.CoreData!.CallStack.Push(frame);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static void PopCallStackFrameWithStackPop(this LuaThread thread)
    {
        var coreData = thread.CoreData!;

        coreData.Stack.PopUntil(coreData!.CallStack.Pop().Base);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static void PopCallStackFrameWithStackPop(this LuaThread thread, int frameBase)
    {
        var coreData = thread.CoreData!;
        coreData!.CallStack.Pop();
        {
            coreData.Stack.PopUntil(frameBase);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static void PopCallStackFrame(this LuaThread thread)
    {
        var coreData = thread.CoreData!;
        coreData!.CallStack.Pop();
    }
}