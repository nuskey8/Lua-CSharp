using Lua.Runtime;
using Lua;
using Lua.Standard;

var state = LuaState.Create();
state.OpenStandardLibraries();
{
    var closure = state.Load("return function (a,b,...)  print('a : '..a..' b :'..'args : ',...) end", "simple");
    using var threadLease = state.MainThread.RentUseThread();
    var thread = threadLease.Thread;
    {
        var count = await thread.RunAsync(closure);
        var results = thread.ReadReturnValues(count);
        for (int i = 0; i < results.Length; i++)
        {
            Console.WriteLine(results[i]);
        }

        var f = results[0].Read<LuaClosure>();
        results.Dispose();
        thread.Push("hello", "world", 1, 2, 3);
        count = await thread.RunAsync(f);
        results = thread.ReadReturnValues(count);
        for (int i = 0; i < results.Length; i++)
        {
            Console.WriteLine(results[i]);
        }

        results.Dispose();
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
            var count = await coroutine.ResumeAsync();
            using var resumeResult = coroutine.ReadReturnValues(count);
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