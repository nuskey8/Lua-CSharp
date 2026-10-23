using Lua.IO;

namespace Lua.Tests.Helpers;

public class TestStandardIO(bool byteStrings = false) : ILuaStandardIO
{
    readonly ConsoleStandardIO consoleStandardIO = new(byteStrings);

    public ILuaStream Input
    {
        get { return consoleStandardIO.Input; }
    }

    // This is a test implementation of Output that writes to the console. Because NUnit does not support Console output streams.

    public ILuaStream Output { get; set; } =
        new StandardIOStream(
            new BufferedOutputStream(
                (memory) =>
                {
                    Console.WriteLine(
                        byteStrings ? LuaByteString.ToString(memory.ToString()) : memory.ToString()
                    );
                }
            )
        );

    public ILuaStream Error
    {
        get { return consoleStandardIO.Error; }
    }
}
