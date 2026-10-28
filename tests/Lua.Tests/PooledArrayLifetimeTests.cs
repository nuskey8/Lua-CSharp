using System.Runtime.CompilerServices;
using Lua.Internal;

namespace Lua.Tests;

public class PooledArrayLifetimeTests
{
    [TestCase(false)]
    [TestCase(true)]
    public void DisposedArraysDoNotKeepReferenceValuesAlive(bool luaValues)
    {
        var payload = AllocateAndDispose(luaValues);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        Assert.That(payload.IsAlive, Is.False);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static WeakReference AllocateAndDispose(bool luaValues)
    {
        var payload = new object();
        if (luaValues)
        {
            using var values = new PooledArray<LuaValue>(16);
            values.AsSpan()[^1] = LuaValue.FromObject(payload);
        }
        else
        {
            using var values = new PooledArray<object>(16);
            values[0] = payload;
        }
        return new WeakReference(payload);
    }
}
