namespace Lua;

public static class LuaThreadExtensions
{
    internal static void ThrowIfCancellationRequested(this LuaState state, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            Throw(state, cancellationToken);
        }

        return;

        static void Throw(LuaState state, CancellationToken cancellationToken)
        {
            throw new LuaCanceledException(state, cancellationToken);
        }
    }
}