using Lua.Runtime;
using Lua.Standard;

namespace Lua.Tests;

public class UpvalueUnwindingTests
{
    [Test]
    public async Task CancellationAfterSuccessfulAsyncCallbackClosesCaptures()
    {
        using var state = LuaState.Create();
        using var cancellation = new CancellationTokenSource();
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        state.Environment["pause"] = new LuaFunction(
            async (context, _) =>
            {
                await gate.Task;
                return context.Return();
            }
        );
        var execution = state
            .DoStringAsync(
                """
                local x = 123
                saved = function() return x end
                pause()
                x = 456
                """,
                cancellationToken: cancellation.Token
            )
            .AsTask();
        Assert.That(execution.IsCompleted, Is.False);
        cancellation.Cancel();
        gate.SetResult();
        Assert.ThrowsAsync<LuaCanceledException>(async () => await execution);
        var saved = state.Environment["saved"].Read<LuaClosure>();
        Assert.That(saved.UpValues[0].IsClosed, Is.True);
        state.Stack.Clear();
        Assert.That((await state.ExecuteAsync(saved))[0], Is.EqualTo(new LuaValue(123)));
    }

    [TestCase(false, false)]
    [TestCase(true, false)]
    [TestCase(false, true)]
    [TestCase(true, true)]
    public async Task NestedFailureClosesAllDiscardedFrames(bool asynchronous, bool protectedCall)
    {
        using var state = LuaState.Create();
        state.OpenStandardLibraries();
        state.Environment["fail"] = new LuaFunction(
            async (context, _) =>
            {
                if (asynchronous)
                    await Task.Yield();
                throw new InvalidOperationException("failure");
            }
        );
        // A non-tail call leaves multiple Lua frames in a single VM invocation.
        var source = """
            local function outer()
                local x = 123
                savedOuter = function() return x end
                local function middle()
                    local y = 456
                    savedMiddle = function() return y end
                    local function inner() fail(); return 0 end
                    inner()
                end
                middle()
            end
            """;
        if (protectedCall)
            await state.DoStringAsync(source + "\npcall(outer)");
        else
            Assert.ThrowsAsync<LuaRuntimeException>(async () =>
                await state.DoStringAsync(source + "\nouter()")
            );

        var outer = state.Environment["savedOuter"].Read<LuaClosure>();
        var middle = state.Environment["savedMiddle"].Read<LuaClosure>();
        Assert.That(outer.UpValues[0].IsClosed, Is.True);
        Assert.That(middle.UpValues[0].IsClosed, Is.True);
        state.Stack.Clear();
        await state.DoStringAsync("local a,b,c,d=7,8,9,10; return a+b+c+d");
        var values = await state.DoStringAsync("return savedOuter(), savedMiddle()");
        Assert.That(values, Is.EqualTo(new LuaValue[] { 123, 456 }));
        Assert.That(state.CallStackFrameCount, Is.Zero);
        Assert.That(state.Stack.Count, Is.Zero);
    }

    [Test]
    public async Task ProtectedFailureKeepsTheSurvivingCallersCaptureOpen()
    {
        using var state = LuaState.Create();
        state.OpenStandardLibraries();
        await state.DoStringAsync(
            """
            local x = 123
            local function saved() return x end
            local function outer()
                local y = 456
                discarded = function() return y end
                local function inner() error('failure') end
                inner()
            end
            assert(not pcall(outer))
            x = 789
            assert(saved() == 789)
            assert(discarded() == 456)
            """
        );
    }
}
