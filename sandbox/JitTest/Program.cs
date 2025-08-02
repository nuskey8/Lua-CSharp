// See https://aka.ms/new-console-template for more information


using System.Reflection;
using System.Runtime.CompilerServices;
using JitInspect;
using Lua;
using Lua.Runtime;
using Lua.Standard;

// dotnet run --configuration Release /p:DefineConstants="CASE_MARKER"
// to activate the CASE_MARKER
// JitInspect can be run in Windows and Linux (MacOS is not supported yet)
var luaState = LuaState.Create();
luaState.OpenStandardLibraries();
var closure = luaState.Load(File.ReadAllBytes(GetAbsolutePath("test.lua")), "test.lua");

for (var i = 0; i < 1000; i++)
{
    await luaState.RunAsync(closure);
    luaState.Stack.Clear();
}

var savePath = GetAbsolutePath("history");
var thisDir = GetThisDirectoryName();
var newJIitPath = Path.Join(thisDir, $"jit_{DateTime.Now:yyyy-MM-dd-HH-mm-ss}.txt");
var lastJitPaths = Directory.GetFiles(thisDir).Where(x => x.Contains("jit_"));
if (!Directory.Exists(savePath))
{
    Directory.CreateDirectory(savePath);
}

if (lastJitPaths.Any())
{
    Console.WriteLine("Last:" + File.ReadAllLines(lastJitPaths.First())[^1]);
    foreach (var jitPath in lastJitPaths)
    {
        var last = jitPath;
        var dest = Path.Join(savePath, Path.GetFileName(jitPath));
        File.Move(last, dest);
    }
}

var method = typeof(LuaVirtualMachine).GetMethod("MoveNext", BindingFlags.Static | BindingFlags.NonPublic)!;
using var disassembler = JitDisassembler.Create();
var nextJitText = disassembler.Disassemble(method, new() { PrintInstructionAddresses = true });
File.WriteAllText(newJIitPath, nextJitText);
//Console.WriteLine("New:" + nextJitText.Split("\n")[^1]);


static string GetThisDirectoryName([CallerFilePath] string callerFilePath = "")
{
    return Path.GetDirectoryName(callerFilePath)!;
}

static string GetAbsolutePath(string relativePath, [CallerFilePath] string callerFilePath = "")
{
    return Path.Join(Path.GetDirectoryName(callerFilePath)!, relativePath);
}