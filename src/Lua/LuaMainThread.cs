namespace Lua;

public sealed class LuaMainThread : LuaThread
{
    public override LuaThreadStatus GetStatus()
    {
        return LuaThreadStatus.Running;
    }

    public override void UnsafeSetStatus(LuaThreadStatus status)
    {
        // Do nothing
    }

    public override ValueTask<int> ResumeAsync(LuaFunctionExecutionContext context, CancellationToken cancellationToken = default)
    {
        return new(context.Return(false, "cannot resume non-suspended coroutine"));
    }

    public override ValueTask<int> YieldAsync(LuaFunctionExecutionContext context, CancellationToken cancellationToken = default)
    {
        throw new LuaRuntimeException(context.State.GetTraceback(), "attempt to yield from outside a coroutine");
    }
}