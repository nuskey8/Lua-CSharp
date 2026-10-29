using System.Runtime.CompilerServices;
using Lua.Platforms;

namespace Lua.Tests;

[TestFixture(LuaStringMode.Utf16)]
[TestFixture(LuaStringMode.Bytes)]
public class ConcatLifetimeTests(LuaStringMode mode)
{
    LuaState CreateState() =>
        LuaState.Create(
            mode == LuaStringMode.Bytes ? LuaPlatform.DefaultByteStrings : LuaPlatform.Default
        );

    [TestCase(false, false)]
    [TestCase(false, true)]
    [TestCase(true, false)]
    [TestCase(true, true)]
    public void FailedConcatenationReleasesOperandsAndPreservesPrefix(
        bool asynchronous,
        bool stackOperands
    )
    {
        using var state = CreateState();
        state.Stack.Push(12345);
        var payload = FailConcatenation(state, asynchronous, stackOperands);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        Assert.That(payload.IsAlive, Is.False);
        Assert.That(state.Stack.AsSpan().ToArray(), Is.EqualTo(new LuaValue[] { 12345 }));
        Assert.That(state.CallStackFrameCount, Is.Zero);
        GC.KeepAlive(state);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static WeakReference FailConcatenation(LuaState state, bool asynchronous, bool stackOperands)
    {
        var payload = new object();
        var value = new LuaTable
        {
            ["payload"] = LuaValue.FromObject(payload),
            Metatable = new LuaTable
            {
                ["__concat"] = new LuaFunction(
                    async (context, _) =>
                    {
                        if (asynchronous)
                            await Task.Yield();
                        throw new LuaRuntimeException(context.State, "failure");
                    }
                ),
            },
        };
        try
        {
            if (stackOperands)
            {
                state.Stack.PushRange([value, "suffix", 42]);
                state.ConcatAsync(3).AsTask().GetAwaiter().GetResult();
            }
            else
                state.ConcatAsync([value, "suffix", 42]).AsTask().GetAwaiter().GetResult();
            Assert.Fail("Expected a Lua error.");
        }
        catch (LuaRuntimeException exception)
        {
            Assert.That(exception.ErrorObject, Is.EqualTo(new LuaValue("failure")));
        }
        return new(payload);
    }

    [Test]
    public void CancellationBeforeConcatenationRestoresPrefixWithoutInvokingMetamethod()
    {
        using var state = CreateState();
        state.Stack.Push(12345);
        using var source = new CancellationTokenSource();
        source.Cancel();
        var value = new LuaTable
        {
            Metatable = new LuaTable
            {
                ["__concat"] = new LuaFunction(
                    (_, _) => throw new AssertionException("Unexpected call.")
                ),
            },
        };
        var exception = Assert.ThrowsAsync<LuaCanceledException>(async () =>
            await state.ConcatAsync([value, "suffix"], source.Token)
        )!;
        Assert.That(exception.CancellationToken, Is.EqualTo(source.Token));
        Assert.That(state.Stack.AsSpan().ToArray(), Is.EqualTo(new LuaValue[] { 12345 }));
        Assert.That(state.CallStackFrameCount, Is.Zero);
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task SuccessfulConcatenationKeepsOneResultAfterPrefix(bool asynchronous)
    {
        using var state = CreateState();
        state.Stack.Push(12345);
        var value = new LuaTable
        {
            Metatable = new LuaTable
            {
                ["__concat"] = new LuaFunction(
                    async (context, _) =>
                    {
                        if (asynchronous)
                            await Task.Yield();
                        return context.Return("joined");
                    }
                ),
            },
        };
        var result = await state.ConcatAsync(["start", value, "end", 42]);
        Assert.That(result, Is.EqualTo(new LuaValue("startjoined")));
        Assert.That(state.Stack.AsSpan().ToArray(), Is.EqualTo(new LuaValue[] { 12345, result }));
        Assert.That(state.CallStackFrameCount, Is.Zero);
    }
}
