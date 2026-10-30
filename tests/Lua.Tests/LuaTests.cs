using System.Text;
using Lua.IO;
using Lua.Platforms;
using Lua.Standard;
using Lua.Tests.Helpers;

namespace Lua.Tests;

[TestFixture(LuaStringMode.Utf16)]
[TestFixture(LuaStringMode.Bytes)]
public class LuaTests(LuaStringMode stringMode)
{
    static string PatchFilesLuaSource(string source, LuaStringMode stringMode)
    {
        source = source.Replace("\r\n", "\n");
        if (stringMode != LuaStringMode.Bytes)
        {
            source = source.Replace("\\xEF\\xBB\\xBF", "\uFEFF");
            // UTF-16 platforms use text I/O. Raw binary file coverage runs in byte mode.
            var start = source.IndexOf("-- loading binary file\n", StringComparison.Ordinal);
            var end = source.IndexOf("-- 'loadfile' with 'env'", start, StringComparison.Ordinal);
            source = source.Remove(start, end - start);
        }
        return source.Replace("if not _noposix then", "_noposix = true; if not _noposix then");
    }

    [Test]
    [Parallelizable(ParallelScope.All)]
    [TestCase("tests-lua/code.lua")]
    [TestCase("tests-lua/goto.lua")]
    [TestCase("tests-lua/constructs.lua")]
    [TestCase("tests-lua/locals.lua")]
    [TestCase("tests-lua/literals.lua")]
    [TestCase("tests-lua/pm.lua")]
    [TestCase("tests-lua/sort.lua")]
    [TestCase("tests-lua/calls.lua")]
    [TestCase("tests-lua/files.lua")]
    [TestCase("tests-lua/closure.lua")]
    [TestCase("tests-lua/errors.lua")]
    [TestCase("tests-lua/events.lua")]
    [TestCase("tests-lua/vararg.lua")]
    [TestCase("tests-lua/nextvar.lua")]
    [TestCase("tests-lua/math.lua")]
    [TestCase("tests-lua/bitwise.lua")]
    [TestCase("tests-lua/strings.lua")]
    [TestCase("tests-lua/coroutine.lua")]
    [TestCase("tests-lua/db.lua")]
    [TestCase("tests-lua/verybig.lua")]
    public async Task Test_Lua(string file)
    {
        var path = FileHelper.GetAbsolutePath(file);
        var baseDirectory = Path.GetDirectoryName(path)!;
        using var state = LuaState.Create(
            stringMode == LuaStringMode.Bytes ? LuaPlatform.DefaultByteStrings : LuaPlatform.Default
        );
        state.Platform = state.Platform with
        {
            StandardIO = new TestStandardIO(stringMode),
            FileSystem = new FileSystem(baseDirectory, stringMode),
        };
        state.OpenStandardLibraries();
        if (file == "tests-lua/errors.lua")
            state.Environment["_soft"] = true;
        try
        {
            if (file == "tests-lua/files.lua")
            {
                // files.lua contains raw 8-bit source literals. Decode its bytes 1:1
                // and apply compatibility patches in memory so the
                // original Lua test file stays untouched on disk.
                var sourceBytes = await File.ReadAllBytesAsync(path);
                var source = PatchFilesLuaSource(
                    Encoding.Latin1.GetString(sourceBytes),
                    stringMode
                );
                var name = "@" + Path.GetFileName(file);
                var closure =
                    stringMode == LuaStringMode.Bytes
                        ? state.Load(LuaByteString.ToBytes(source), name)
                        : state.Load(source, name);
                await state.ExecuteAsync(closure);
            }
            else
            {
                await state.DoFileAsync(Path.GetFileName(file));
            }
        }
        catch (LuaRuntimeException e)
        {
            var luaTraceback = e.LuaTraceback;
            if (luaTraceback == null)
            {
                throw;
            }

            var line = luaTraceback.FirstLine;
            throw new($"{path}:{line} \n{e.InnerException}\n {e}");
        }
    }
}
