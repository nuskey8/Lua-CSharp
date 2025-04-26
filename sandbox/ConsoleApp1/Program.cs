using System.Runtime.CompilerServices;
using Lua.CodeAnalysis.Compilation;
using Lua.Runtime;
using Lua;
using Lua.Standard;

var state = LuaState.Create();
state.OpenStandardLibraries();

state.Environment["vec3"] = new LVec3();

try
{
    var source = File.ReadAllText(GetAbsolutePath("test.lua"));


    Console.WriteLine("Source Code " + new string('-', 50));

    Console.WriteLine(source);

    var closure = state.Compile(source, "test.lua");

    DebugChunk(closure.Proto, 0);

    Console.WriteLine("Output " + new string('-', 50));

    using var results = await state.RunAsync(closure);

    Console.WriteLine("Result " + new string('-', 50));

    for (int i = 0; i < results.Count; i++)
    {
        Console.WriteLine(results[i]);
    }

    Console.WriteLine("End " + new string('-', 50));
}
catch (Exception ex)
{
    Console.WriteLine(ex);
    if (ex is LuaRuntimeException { InnerException: not null } luaEx)
    {
        Console.WriteLine(luaEx.InnerException);
    }
}

static string GetAbsolutePath(string relativePath, [CallerFilePath] string callerFilePath = "")
{
    return Path.Combine(Path.GetDirectoryName(callerFilePath)!, relativePath);
}

static void DebugChunk(Prototype chunk, int id)
{
    Console.WriteLine($"Chunk[{id}]" + new string('=', 50));
    Console.WriteLine($"Parameters:{chunk.ParameterCount}");

    Console.WriteLine("Code " + new string('-', 50));
    var index = 0;
    foreach (var inst in chunk.Code)
    {
        Console.WriteLine($"[{index}]\t{chunk.LineInfo[index]}\t\t{inst}");
        index++;
    }

    Console.WriteLine("LocalVariables " + new string('-', 50));
    index = 0;
    foreach (var local in chunk.LocalVariables)
    {
        Console.WriteLine($"[{index}]\t{local.Name}\t{local.StartPc}\t{local.EndPc}");
        index++;
    }

    Console.WriteLine("Constants " + new string('-', 50));
    index = 0;
    foreach (var constant in chunk.Constants.ToArray())
    {
        Console.WriteLine($"[{index}]\t{constant}");
        index++;
    }

    Console.WriteLine("UpValues " + new string('-', 50));
    index = 0;
    foreach (var upValue in chunk.UpValues.ToArray())
    {
        Console.WriteLine($"[{index}]\t{upValue.Name}\t{(upValue.IsLocal ? 1 : 0)}\t{upValue.Index}");
        index++;
    }

    Console.WriteLine();

    var nestedChunkId = 0;
    foreach (var localChunk in chunk.ChildPrototypes)
    {
        DebugChunk(localChunk, nestedChunkId);
        nestedChunkId++;
    }
}