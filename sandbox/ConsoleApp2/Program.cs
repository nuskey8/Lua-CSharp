using Lua.Runtime;
using Lua;
using Lua.Standard;
using System;

var state = LuaGlobalState.Create();
state.OpenStandardLibraries();
{
    var closure = state.Load("return function (a,b,...)  print('a : '..a..' b :'..'args : ',...) end", "@simple");
    using var threadLease = state.MainThread.RentUserThread();
    var access = threadLease.State.RootAccess;
    {
        var count = await access.RunAsync(closure, 0);
        var results = access.ReadTopValues(count);
        for (var i = 0; i < results.Length; i++)
        {
            Console.WriteLine(results[i]);
        }

        var f = results[0].Read<LuaClosure>();
        results.Dispose();
        access.Push("hello", "world", 1, 2, 3);
        count = await access.RunAsync(f, 5);
        results = access.ReadTopValues(count);
        for (var i = 0; i < results.Length; i++)
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
                print('In Lua:', coroutine.yield('from lua', i,v))
            end
        end
        """, "coroutine");
    var f = results[0].Read<LuaClosure>();
    using var coroutineLease = state.MainThread.RentCoroutine(f);
    var coroutine = coroutineLease.Thread;
    {
        var stack = new LuaStack();
        stack.PushRange("a", "b", "c", "d", "e");

        for (var i = 0; coroutine.CanResume; i++)
        {
            if (i != 0)
            {
                stack.Push("from C# ");
                stack.Push(i);
            }

            await coroutine.ResumeAsync(stack);
            Console.Write("In C#:\t");
            for (var j = 1; j < stack.Count; j++)
            {
                Console.Write(stack[j]);
                Console.Write('\t');
            }

            Console.WriteLine();
            stack.Clear();
        }
    }
}