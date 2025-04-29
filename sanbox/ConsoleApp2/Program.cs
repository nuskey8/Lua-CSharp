using Lua.Runtime;
using Lua;
using Lua.Standard;

var state = LuaState.Create();
state.OpenStandardLibraries();
{
    var closure = state.Compile("return function (a,b,...)  print('a : '..a..' b :'..'args : ',...) end", "simple");
    using var threadLease = state.MainThread.RentUseThread();
    var thread = threadLease.Thread;
    {
        var results = await thread.RunAsync(closure, 0);
        for (int i = 0; i < results.Length; i++)
        {
            Console.WriteLine(results[i]);
        }

        var f = results[0].Read<LuaClosure>();
        results.Dispose();
        thread.Push("hello", "world", 1, 2, 3);
        var result2 = await thread.RunAsync(f, 5);
        for (int i = 0; i < result2.Length; i++)
        {
            Console.WriteLine(result2[i]);
        }

        result2.Dispose();
    }
}

{
    var results = await state.DoStringAsync(
        """
        return function (...)  
            local args = {...}
            for i = 1, #args do
                local v = args[i]
                print('To Lua:\t' .. coroutine.yield('from C# ' .. i ..' '..v))
            end
        end
        """, "coroutine");
    var f = results[0].Read<LuaClosure>();
    using var coroutineLease = state.MainThread.RentCoroutine(f);
    var coroutine = coroutineLease.Thread;
    {
        coroutine.Push("a", "b", "c", "d", "e");

        for (int i = 0; coroutine.CanResume; i++)
        {
            if (i != 0) coroutine.Push($"from C# {i}");
            using var resumeResult = await coroutine.ResumeAsync();
            Console.Write("To C#:\t");
            for (int j = 0; j < resumeResult.Length; j++)
            {
                Console.Write(resumeResult[j]);
                Console.Write('\t');
            }

            Console.WriteLine();
        }
    }
}