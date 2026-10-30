using System.Diagnostics.Tracing;
using Lua.Platforms;
using Lua.Standard;

namespace Lua.Tests;

[TestFixture(LuaStringMode.Utf16)]
[TestFixture(LuaStringMode.Bytes)]
public class DateBufferTests(LuaStringMode mode)
{
    [TestCase("%Q")]
    [TestCase("%")]
    [TestCase("%E")]
    [NonParallelizable]
    public void InvalidFormatsReturnEveryRentedBuffer(string suffix)
    {
        using var state = LuaState.Create(
            mode == LuaStringMode.Bytes ? LuaPlatform.DefaultByteStrings : LuaPlatform.Default
        );
        state.OpenStandardLibraries();
        state.Environment["format"] = new string('x', 4096) + suffix;
        var closure = state.Load("assert(not pcall(os.date,format))", "@date-buffer.lua");
        using var events = new PoolEvents();
        // Exclude one-time runtime initialization from buffer ownership tracking.
        state.ExecuteAsync(closure, Memory<LuaValue>.Empty).GetAwaiter().GetResult();
        events.ThreadId = Environment.CurrentManagedThreadId;
        try
        {
            state.ExecuteAsync(closure, Memory<LuaValue>.Empty).GetAwaiter().GetResult();
        }
        finally
        {
            events.ThreadId = 0;
        }
        Assert.That(events.Rents, Is.GreaterThan(0));
        Assert.That(events.Outstanding, Is.Empty);
    }

    sealed class PoolEvents : EventListener
    {
        public int ThreadId;
        public int Rents;
        public readonly HashSet<(int Buffer, int Pool)> Outstanding = new();

        protected override void OnEventSourceCreated(EventSource source)
        {
            if (source.Name == "System.Buffers.ArrayPoolEventSource")
                EnableEvents(source, EventLevel.Verbose);
        }

        protected override void OnEventWritten(EventWrittenEventArgs e)
        {
            if (ThreadId == 0 || ThreadId != Environment.CurrentManagedThreadId)
                return;
            if (e.EventName == "BufferRented")
            {
                Rents++;
                Outstanding.Add(((int)e.Payload![0]!, (int)e.Payload[2]!));
            }
            else if (e.EventName == "BufferReturned")
            {
                Outstanding.Remove(((int)e.Payload![0]!, (int)e.Payload[2]!));
            }
        }
    }
}
