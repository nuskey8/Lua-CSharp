using System;
using Lua;
using Lua.IO;
using Lua.Loaders;
using Lua.Platforms;
using Lua.Standard;
using Lua.Unity;
using UnityEngine;

public class Sandbox : MonoBehaviour
{
    async void Start()
    {
        Application.targetFrameRate = 60;
        var state = LuaState.Create(new LuaPlatform(
            fileSystem: CompositeLoaderFileSystem.Create(new FileSystem(),
                new AddressablesLuaFileLoader(),
                new ResourcesLuaFileLoader()
            ),
            osEnvironment: new UnityApplicationOsEnvironment(allowToQuitOnExitCall:true),
            standardIO: new UnityStandardIO()
        ));
        state.OpenStandardLibraries();
        state.Environment["package"].Read<LuaTable>()["path"] += ";?.luac;" + Application.streamingAssetsPath + "/?.lua;";
        try
        {
            await state.DoStringAsync(
                @"
print('test start')
local foo = require 'foo'
foo.greet()

local bar = require 'bar'
bar.greet()

require 'test'
require 'streaming_asset'
os.exit(0)
", cancellationToken: destroyCancellationToken);
        }
        catch (Exception ex)
        {
            if (ex is LuaCanceledException exception)
            {
                Debug.LogError(exception.LuaTraceback?.ToString());
            }
            else Debug.LogException(ex);
        }
    }
}