using Lua;
using Lua.Standard;

try
{
    LuaState state = LuaState.Create();

    state.OpenBasicLibrary();
    state.OpenStringLibrary();
    state.OpenMathLibrary();
    state.OpenTableLibrary();
    state.OpenModuleLibrary();
    state.OpenStringBufferLibrary();

    await state.DoFileAsync("D:/repos/Lua-CSharp/sandbox/StringBufferTest/StringBufferTest.lua");

    Console.WriteLine("press Enter to exit");
    Console.Read();
}
catch (Exception e)
{
    Console.WriteLine($"Error: {e}");
}
