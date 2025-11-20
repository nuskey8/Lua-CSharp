using System;
using Lua;
using Lua.IO;
using Lua.Loaders;
using Lua.Platforms;
using Lua.Standard;
using Lua.Unity;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class Sandbox : MonoBehaviour
{
    async void Start()
    {
        
        var state = LuaState.Create( new LuaPlatform(
            FileSystem: new FileSystem(Application.streamingAssetsPath),
            OsEnvironment: new UnityApplicationOsEnvironment(),
            StandardIO: new UnityStandardIO(),
            TimeProvider: TimeProvider.System
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

local s =require 'streaming'
s.f()
", cancellationToken: destroyCancellationToken);
        }
        catch (Exception ex)
        {
            Debug.LogException(ex);
        }
    }
    
    MeshTopology[ ] topologies = Enum.GetValues(typeof(MeshTopology)) as MeshTopology[];
    public bool ContainsTriangle;

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Space))
        {
            ContainsTriangle=(topologies.Contains( MeshTopology.Points));
            
            Debug.Break();
        }
        
        if (Input.GetKeyDown(KeyCode.A))
        {
            ContainsTriangle=(ContainsInArray(topologies, MeshTopology.Points));
            
            Debug.Break();
        }
    }
    
    bool ContainsInArray<T>(T[] array, T value)
    {
        foreach (var item in array)
        {
            if (EqualityComparer<T>.Default.Equals(item, value))
            {
                return true;
            }
        }
        return false;
    }
}