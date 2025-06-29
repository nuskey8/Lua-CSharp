using Lua.IO;

namespace Lua.Tests.Helpers
{
    public class TestStandardIO :ILuaStandardIO
    {
        private readonly ConsoleStandardIO consoleStandardIO = new ConsoleStandardIO();
        public ILuaStream Input => consoleStandardIO.Input;
        // This is a test implementation of Output that writes to the console. Because NUnit does not support Console output streams.
        
        public ILuaStream Output { get; set; } = new StandardIOStream(new BufferedOutputStream((memory) => { Console.WriteLine(memory.ToString()); }));
        public ILuaStream Error  => consoleStandardIO.Error;
    }
}