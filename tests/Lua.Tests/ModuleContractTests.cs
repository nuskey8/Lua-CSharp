using Lua.IO;
using Lua.Platforms;
using Lua.Standard;
using Lua.Tests.Helpers;

namespace Lua.Tests;

[TestFixture(LuaStringMode.Utf16)]
[TestFixture(LuaStringMode.Bytes)]
public class ModuleContractTests(LuaStringMode stringMode)
{
    [TestCase("return", "true", 1)]
    [TestCase("return nil", "true", 1)]
    [TestCase("return false", "false", 2)]
    [TestCase("package.loaded.test=42", "42", 1)]
    [TestCase("package.loaded.test=false", "false", 2)]
    [TestCase("package.loaded.test=42;return 43", "43", 1)]
    public async Task RequireUsesTheLoaderResultAndPackageLoaded(
        string loader,
        string expected,
        int expectedCalls
    )
    {
        using var state = CreateState();
        var result = await state.DoStringAsync(
            $$"""
            local calls=0
            package.preload.test=function() calls=calls+1;{{loader}} end
            local first=require('test')
            local second=require('test')
            return first=={{expected}},second=={{expected}},package.loaded.test=={{expected}},calls
            """
        );
        Assert.That(result, Is.EqualTo(new LuaValue[] { true, true, true, expectedCalls }));
    }

    [Test]
    public async Task RequirePassesSearcherDataAndStopsAtTheFirstNilSearcher()
    {
        using var state = CreateState();
        await state.DoStringAsync(
            """
            local data={}
            local loader=function(name,searchData)
                assert(name=='test' and searchData==data)
                return 42,99
            end
            package.searchers={function(name) return loader,data end}
            assert(require('test')==42)
            package.searchers={[2]=function() error('must not run') end}
            local ok,e=pcall(require,'missing')
            assert(not ok and e:find("module 'missing' not found:",1,true))
            """
        );
    }

    [Test]
    public async Task FileLoaderReceivesItsNameAndPath()
    {
        using var state = CreateState();
        state.Platform = state.Platform with { FileSystem = new ModuleFileSystem() };
        var result = await state.DoStringAsync("return require('test')");
        Assert.That(result, Is.EqualTo(new LuaValue[] { true }));
    }

    [Test]
    public async Task SearchPathUsesDefaultsAndReturnsFailureDetails()
    {
        using var state = CreateState();
        state.Platform = state.Platform with { FileSystem = new ModuleFileSystem() };
        await state.DoStringAsync(
            """
            assert(package.searchpath('test',';;?.lua;;')=='test.lua')
            assert(package.searchpath('test','?.lua',nil,nil)=='test.lua')
            local path,message=package.searchpath('missing',';;?.lua;?/init.lua;;')
            assert(path==nil and message=="\n\tno file 'missing.lua'\n\tno file 'missing/init.lua'")
            local ok,e=pcall(require,'missing')
            assert(not ok and e:find("no field package.preload['missing']",1,true))
            assert(e:find("no file 'missing.lua'",1,true))
            """
        );
    }

    LuaState CreateState()
    {
        var state = LuaState.Create(
            stringMode == LuaStringMode.Bytes ? LuaPlatform.DefaultByteStrings : LuaPlatform.Default
        );
        state.OpenStandardLibraries();
        return state;
    }

    sealed class ModuleFileSystem : NotImplementedExceptionFileSystemBase
    {
        public override string DirectorySeparator => "/";

        public override bool IsReadable(string path) => path == "test.lua";

        public override ValueTask<ILuaStream> Open(
            string path,
            LuaFileOpenMode mode,
            CancellationToken cancellationToken = default
        ) =>
            new(
                ILuaStream.CreateFromString(
                    "local name,path=...;assert(name=='test' and path=='test.lua');return true"
                )
            );
    }
}
