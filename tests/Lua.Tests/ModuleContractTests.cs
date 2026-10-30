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

    [TestCase(false)]
    [TestCase(true)]
    public async Task RequireHonorsLoadedModuleMetamethods(bool asynchronous)
    {
        using var state = CreateState();
        var suspension = new ManualSuspension();
        state.Environment["pause"] = new LuaFunction(
            async (context, _) =>
            {
                if (asynchronous)
                    await suspension;
                return context.Return();
            }
        );
        var operation = state
            .DoStringAsync(
                """
                local writes=0
                setmetatable(package.loaded,{
                    __index=function(t,key)
                        if key=='cached' then pause();return 42 end
                    end,
                    __newindex=function(t,key,value)
                        writes=writes+1
                        if key=='loaded' then
                            pause()
                            rawset(t,key,value+1)
                        else
                            assert(key=='empty' and value==true)
                            rawset(t,key,43)
                        end
                    end
                })
                package.preload.loaded=function() return 41 end
                package.preload.empty=function() end
                assert(require('cached')==42)
                assert(require('loaded')==42)
                assert(require('empty')==true and require('empty')==43)
                assert(writes==2)
                """
            )
            .AsTask();
        if (asynchronous)
        {
            suspension.Resume();
            suspension.Resume();
        }
        await operation;
        Assert.That(state.Stack.Count, Is.Zero);
        Assert.That(state.CallStackFrameCount, Is.Zero);
        Assert.That(state.NonYieldableCallCount, Is.Zero);
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task RequireAndFileSearcherRetainTheirPackage(bool reopen)
    {
        using var state = CreateState();
        state.Platform = state.Platform with { FileSystem = new ModuleFileSystem() };
        await state.DoStringAsync(
            "savedRequire=require;savedSearcher=package.searchers[2];savedPackage=package"
        );
        if (reopen)
            state.OpenModuleLibrary();
        else
            state.Environment["package"] = new LuaTable();
        await state.DoStringAsync(
            """
            package.path='missing/?.lua'
            savedPackage.preload.preloaded=function() return 42 end
            local _,value=debug.getupvalue(savedRequire,1)
            assert(value==savedPackage)
            _,value=debug.getupvalue(savedSearcher,1)
            assert(value==savedPackage)
            assert(savedRequire('preloaded')==42)
            local f,path=savedSearcher('test')
            assert(type(f)=='function' and path=='test.lua' and f('test',path)==true)
            assert(savedRequire('test')==true)
            """
        );
    }

    [Test]
    public async Task PackageAndPreloadFieldsUseNormalTableAccess()
    {
        using var state = CreateState();
        state.Platform = state.Platform with { FileSystem = new ModuleFileSystem() };
        await state.DoStringAsync(
            """
            local searchers=package.searchers
            package.searchers=nil
            package.path=nil
            setmetatable(package,{__index=function(t,key)
                if key=='searchers' then return searchers end
                if key=='path' then return '?.lua' end
            end})
            setmetatable(package.preload,{__index=function(t,key)
                if key=='virtual' then return function(name) assert(name==key);return 42 end end
            end})
            assert(require('virtual')==42)
            assert(require('test')==true)
            """
        );
    }

    [TestCase("package.preload.test=function() coroutine.yield('loader') end", "require")]
    [TestCase("package.searchers={function() coroutine.yield('searcher') end}", "require")]
    [TestCase(
        "setmetatable(package.loaded,{__index=function() coroutine.yield('index') end})",
        "require"
    )]
    [TestCase(
        "package.preload.test=function() return 42 end;setmetatable(package.loaded,{__newindex=function() coroutine.yield('newindex') end})",
        "require"
    )]
    [TestCase(
        "setmetatable(package.preload,{__index=function() coroutine.yield('preload') end})",
        "package.searchers[1]"
    )]
    [TestCase(
        "package.path=nil;setmetatable(package,{__index=function() coroutine.yield('path') end})",
        "package.searchers[2]"
    )]
    public async Task ModuleCallbacksCannotYieldAndRestoreYieldability(
        string setup,
        string function
    )
    {
        using var state = CreateState();
        await state.DoStringAsync(
            setup
                + $$"""
                ;local co=coroutine.create(function()
                    local ok,e=pcall({{function}},'test')
                    assert(not ok and e:find('attempt to yield across a C-call boundary',1,true))
                    coroutine.yield('after require')
                    return 42
                end)
                local ok,value=coroutine.resume(co)
                assert(ok and value=='after require')
                ok,value=coroutine.resume(co)
                assert(ok and value==42 and coroutine.status(co)=='dead')
                """
        );
        Assert.That(state.Stack.Count, Is.Zero);
        Assert.That(state.CallStackFrameCount, Is.Zero);
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
