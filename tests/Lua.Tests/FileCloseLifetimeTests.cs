using System.Runtime.CompilerServices;
using Lua.IO;
using Lua.Platforms;
using Lua.Standard;

namespace Lua.Tests;

[TestFixture(LuaStringMode.Utf16)]
[TestFixture(LuaStringMode.Bytes)]
public class FileCloseLifetimeTests(LuaStringMode mode)
{
    [TestCase(false, false)]
    [TestCase(true, false)]
    [TestCase(false, true)]
    [TestCase(true, true)]
    public void ClosedHandlesReleaseTheirStreamWhenCleanupReportsAnError(
        bool flushFailure,
        bool disposeFailure
    )
    {
        using var state = LuaState.Create(
            mode == LuaStringMode.Bytes ? LuaPlatform.DefaultByteStrings : LuaPlatform.Default
        );
        state.OpenStandardLibraries();
        var stream = InstallAndClose(state, mode, flushFailure, disposeFailure);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        Assert.That(stream.IsAlive, Is.False);
        Assert.That(state.Environment["file"].Read<FileHandle>().IsOpen, Is.False);
        Assert.That(state.Stack.Count, Is.Zero);
        Assert.That(state.CallStackFrameCount, Is.Zero);
        GC.KeepAlive(state);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static WeakReference InstallAndClose(
        LuaState state,
        LuaStringMode mode,
        bool flushFailure,
        bool disposeFailure
    )
    {
        var stream = new CleanupStream(flushFailure, disposeFailure);
        state.Environment["file"] = LuaValue.FromUserData(
            new FileHandle(new LuaStream(LuaFileOpenMode.Write, stream, mode))
        );
        var result = state.DoStringAsync("return file:close()").GetAwaiter().GetResult();
        Assert.That(
            result[0],
            Is.EqualTo(flushFailure || disposeFailure ? LuaValue.Nil : new LuaValue(true))
        );
        Assert.That(stream.CanWrite, Is.False);
        Assert.That(stream.DisposeCount, Is.EqualTo(1));
        return new WeakReference(stream);
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task FailedCloseKeepsAnOpenStreamAvailableForRetry(bool cancel)
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var stream = new RetryStream();
        var file = new FileHandle(stream);
        if (cancel)
            Assert.CatchAsync<OperationCanceledException>(async () =>
                await file.Close(cancellation.Token)
            );
        else
            Assert.ThrowsAsync<IOException>(async () => await file.Close(default));
        Assert.That(file.IsOpen, Is.True);
        await file.Close(default);
        Assert.That(file.IsOpen, Is.False);
        Assert.That(stream.CloseCount, Is.EqualTo(2));
    }

    sealed class CleanupStream(bool flushFailure, bool disposeFailure) : MemoryStream
    {
        public int DisposeCount { get; private set; }

        public override void Flush()
        {
            if (flushFailure)
                throw new IOException("flush failed");
        }

        protected override void Dispose(bool disposing)
        {
            DisposeCount++;
            base.Dispose(disposing);
            if (disposeFailure)
                throw new IOException("dispose failed");
        }
    }

    sealed class RetryStream : ILuaStream
    {
        public LuaFileOpenMode Mode => LuaFileOpenMode.Write;
        public bool IsOpen { get; private set; } = true;
        public int CloseCount { get; private set; }

        public async ValueTask CloseAsync(CancellationToken cancellationToken)
        {
            CloseCount++;
            await Task.Yield();
            if (CloseCount == 1)
            {
                cancellationToken.ThrowIfCancellationRequested();
                throw new IOException("close failed while open");
            }
            Dispose();
        }

        public void Dispose() => IsOpen = false;
    }
}
