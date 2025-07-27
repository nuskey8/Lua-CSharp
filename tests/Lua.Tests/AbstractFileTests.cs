using Lua.IO;
using Lua.Platforms;
using Lua.Standard;
using Lua.Tests.Helpers;

namespace Lua.Tests;

public class AbstractFileTests
{
    class ReadOnlyFileSystem(Dictionary<string, string> dictionary) : NotImplementedExceptionFileSystemBase
    {
        public override ValueTask<ILuaStream> Open(string path, LuaFileOpenMode mode, CancellationToken cancellationToken)
        {
            if (!dictionary.TryGetValue(path, out var value))
            {
                throw new FileNotFoundException($"File {path} not found");
            }

            if (mode != LuaFileOpenMode.Read)
            {
                throw new IOException($"File {path} not opened in read mode");
            }

            return new(ILuaStream.CreateFromMemory(value.AsMemory()));
        }
    }

    [Test]
    public async Task ReadLinesTest()
    {
        var fileContent = "line1\nline2\r\nline3";
        var fileSystem = new ReadOnlyFileSystem(new() { { "test.txt", fileContent } });
        var state = LuaState.Create(new(
            fileSystem: fileSystem,
            osEnvironment: null!,
            standardIO: new ConsoleStandardIO(),
            timeProvider: TimeProvider.System
        ));
        state.OpenStandardLibraries();
        try
        {
            await state.DoStringAsync(
                """
                local lines = {}
                for line in io.lines("test.txt") do
                  table.insert(lines, line)
                  print(line)
                end
                assert(#lines == 3, "Expected 3 lines")
                assert(lines[1] == "line1", "Expected line1")
                assert(lines[2] == "line2", "Expected line2")
                assert(lines[3] == "line3", "Expected line3")
                """);
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            throw;
        }
    }

    [Test]
    public async Task ReadFileTest()
    {
        var fileContent = "Hello, World!";
        var fileSystem = new ReadOnlyFileSystem(new() { { "test.txt", fileContent } });
        var state = LuaState.Create(new(
            fileSystem: fileSystem,
            osEnvironment: null!,
            standardIO: new ConsoleStandardIO(),
            timeProvider: TimeProvider.System));
        state.OpenStandardLibraries();

        await state.DoStringAsync(
            """
            local file = io.open("test.txt", "r")
            assert(file, "Failed to open file")
            local content = file:read("*a")
            assert(content == "Hello, World!", "Expected 'Hello, World!'")
            file:close()
            file = io.open("test2.txt", "r")
            assert(file == nil, "Expected file to be nil")
            """);
    }
}