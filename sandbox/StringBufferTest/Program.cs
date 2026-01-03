using Lua;
using Lua.Standard;

try
{
    LuaState state = LuaState.Create();

    state.OpenBasicLibrary();
    state.OpenModuleLibrary();
    state.OpenStringBufferLibrary();

    var results = await state.DoFileAsync("D:/repos/Lua-CSharp/sandbox/StringBufferTest/StringBufferTest.lua");
}
catch (Exception e)
{
    Console.WriteLine($"Error: {e}");
}

Console.WriteLine("press any key to exit");
Console.Read();
