using Lua.IO;
using Lua.Standard;

namespace Lua.Tests;

public class FileSeekTests
{
    [TestCase("'3'", 3d)]
    [TestCase("'3000000000'", 3000000000d)]
    [TestCase("'0x10'", 16d)]
    public async Task LuaSeekCoercesNumericStrings(string offset, double expected)
    {
        using var state = LuaState.Create();
        state.OpenStandardLibraries();
        using var stream = new PositionStream();
        state.Environment["file"] = new LuaValue(new FileHandle(stream));
        var result = await state.DoStringAsync($"return file:seek('set',{offset})");
        Assert.That(result, Is.EqualTo(new LuaValue[] { expected }));
    }

    [TestCase("1.5")]
    [TestCase("'1.5'")]
    [TestCase("0/0")]
    [TestCase("math.huge")]
    [TestCase("-math.huge")]
    [TestCase("9223372036854775808")]
    public async Task LuaSeekRejectsFractionalAndUnrepresentableOffsets(string offset)
    {
        using var state = LuaState.Create();
        state.OpenStandardLibraries();
        using var stream = new PositionStream();
        state.Environment["file"] = new LuaValue(new FileHandle(stream));
        await state.DoStringAsync(
            $"local ok,e=pcall(file.seek,file,'set',{offset});assert(not ok and e:find('integer',1,true))"
        );
    }

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
