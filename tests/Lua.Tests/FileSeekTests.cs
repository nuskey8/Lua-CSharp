using Lua.IO;
using Lua.Standard;

namespace Lua.Tests;

public class FileSeekTests
{
    [Test]
    public async Task LuaSeekAcceptsOffsetsBeyondInt32()
    {
        using var state = LuaState.Create();
        state.OpenStandardLibraries();
        using var stream = new PositionStream();
        state.Environment["file"] = new LuaValue(new FileHandle(stream));
        var result = await state.DoStringAsync(
            "return file:seek('set',3000000000),file:seek(),file:seek('cur',-2999999999)"
        );
        Assert.That(result, Is.EqualTo(new LuaValue[] { 3000000000d, 3000000000d, 1 }));
    }

    sealed class PositionStream : ILuaStream
    {
        long position;
        public bool IsOpen { get; private set; } = true;
        public LuaFileOpenMode Mode => LuaFileOpenMode.Read;

        public void Dispose() => IsOpen = false;

        public long Seek(SeekOrigin origin, long offset) =>
            position = origin switch
            {
                SeekOrigin.Begin => offset,
                SeekOrigin.Current => position + offset,
                _ => throw new NotSupportedException(),
            };
    }
}
