using Lua.Internal;
using Lua.Runtime;

namespace Lua;

public abstract class LuaThread
{
    public abstract LuaThreadStatus GetStatus();
    public abstract ValueTask<int> Resume(LuaFunctionExecutionContext context, Memory<LuaValue> buffer, CancellationToken cancellationToken = default);
    public abstract ValueTask Yield(LuaFunctionExecutionContext context, CancellationToken cancellationToken = default);

    LuaStack stack = new();
    FastStackCore<CallStackFrame> callStack;

    internal LuaStack Stack => stack;

    public CallStackFrame GetCurrentFrame()
    {
        return callStack.Peek();
    }

    public ReadOnlySpan<LuaValue> GetStackValues()
    {
        return stack.AsSpan();
    }

    internal Tracebacks GetTracebacks()
    {
        return new()
        {
            StackFrames = callStack.AsSpan()[1..].ToArray()
        };
    }

    internal void PushCallStackFrame(CallStackFrame frame)
    {
        callStack.Push(frame);
    }

    internal void PopCallStackFrame()
    {
        var frame = callStack.Pop();
        stack.PopUntil(frame.Base);
    }

    internal void DumpStackValues()
    {
        var span = GetStackValues();
        for (int i = 0; i < span.Length; i++)
        {
            Console.WriteLine($"LuaStack [{i}]\t{span[i]}");
        }
    }
}