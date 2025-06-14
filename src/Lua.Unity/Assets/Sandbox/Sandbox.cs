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
        var state = LuaState.Create( new LuaPlatform(
            fileSystem: new FileSystem(),
            osEnvironment: new UnityApplicationOsEnvironment(),
            standardIO: new UnityStandardIO(),
            timeProvider: TimeProvider.System
        ));
        state.ModuleLoader = CompositeModuleLoader.Create(new AddressablesModuleLoader(), new ResourcesModuleLoader());
        state.OpenStandardLibraries();
        state.Environment["print"] = new LuaFunction("print", (context, ct) =>
        {
            Debug.Log(context.GetArgument<string>(0));
            return new(0);
        });

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
os.exit(0)
", cancellationToken: destroyCancellationToken);
        }
        catch (Exception ex)
        {
            Debug.LogException(ex);
        }
    }
}