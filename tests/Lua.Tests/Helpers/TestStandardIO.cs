using Lua.IO;

namespace Lua.Tests.Helpers;

public class TestStandardIO : ILuaStandardIO
{
    readonly ConsoleStandardIO consoleStandardIO = new();

    public ILuaStream Input
    {
        get
        {
            return consoleStandardIO.Input;
        }
    }
    // This is a test implementation of Output that writes to the console. Because NUnit does not support Console output streams.

    public ILuaStream Output { get; set; } = new StandardIOStream(new BufferedOutputStream((memory) => { Console.WriteLine(memory.ToString()); }));

    public ILuaStream Error
    {
        get
        {
            return consoleStandardIO.Error;
        }
    }
}