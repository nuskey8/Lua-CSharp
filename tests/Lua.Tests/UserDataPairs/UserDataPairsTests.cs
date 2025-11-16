// Copyright (C) 2021-2025 Steffen Itterheim
// Refer to included LICENSE file for terms and conditions.

using Lua.Runtime;
using Lua.Standard;
using Lua.Tests.Helpers;

namespace Lua.Tests.UserDataPairs;

public class UserDataPairsTests
{
    [TestCase("userdatapairs.lua")]
    public async Task Test_UserDataPairs(string file)
    {
        var state = LuaState.Create();
        state.Platform.StandardIO = new TestStandardIO();
        state.OpenStandardLibraries();
        state.Environment["LuaList"] = new LuaValue(new LuaList<int>());

        var path = FileHelper.GetAbsolutePath(file);
        Directory.SetCurrentDirectory(Path.GetDirectoryName(path)!);
        try
        {
            await state.DoFileAsync(Path.GetFileName(file));
        }
        catch (LuaRuntimeException e)
        {
            var luaTraceback = e.LuaTraceback;
            if (luaTraceback == null)
            {
                throw;
            }

            var line = luaTraceback.FirstLine;
            throw new($"{path}:{line} \n{e.InnerException}\n {e}");
        }
    }
}


public sealed class LuaList<T> : ILuaUserData, ILuaEnumerable
{
    static readonly LuaFunction __len = new(Metamethods.Len, (context, _) =>
    {
        return new ValueTask<int>(context.Return(3));
    });
    static readonly LuaFunction __pairs = new(Metamethods.Pairs, (context, _) =>
    {
        var arg0 = context.GetArgument(0);
        return new ValueTask<int>(context.Return(LuaListIterator, arg0, LuaValue.Nil));
    });

    static readonly LuaFunction LuaListIterator = new LuaFunction("listnext", (context, token) =>
    {
        var list = context.GetArgument<LuaList<T>>(0);
        var key = context.HasArgument(1) ? context.Arguments[1] : LuaValue.Nil;

        var index = -1;
        if (key.Type is LuaValueType.Nil)
        {
            index = 0;
        }
        else if (key.TryRead(out int number) && number > 0 && number < list.ManagedArray.Length)
        {
            index = number;
        }

        if (index != -1)
        {
            return new(context.Return(++index, list.ManagedArray[index - 1]));
        }

        return new(context.Return(LuaValue.Nil));
    });

    static LuaTable s_Metatable;
    public LuaTable Metatable { get => s_Metatable; set => throw new NotImplementedException(); }

    public int[] ManagedArray { get; }
    //public Dictionary<string, bool> ManagedDict { get; }
    public LuaList()
    {
        ManagedArray = new [] { 1,2,3,4,5 };
        //ManagedDict = new Dictionary<string, bool> {{"TRUE", true}, {"FALSE", false}};

        s_Metatable = new LuaTable();
        s_Metatable[Metamethods.Len] = __len;
        s_Metatable[Metamethods.Pairs] = __pairs;
        s_Metatable[Metamethods.IPairs] = __pairs;
    }

    public bool TryGetNext(LuaValue key, out KeyValuePair<LuaValue, LuaValue> pair)
    {
        var index = -1;
        if (key.Type is LuaValueType.Nil)
        {
            index = 0;
        }
        else if (key.TryRead(out int integer) && integer > 0 && integer <= ManagedArray.Length)
        {
            index = integer;
        }

        if (index != -1)
        {
            var span = ManagedArray.AsSpan(index);
            for (var i = 0; i < span.Length; i++)
            {
                pair = new(index + i + 1, span[i]);
                return true;
            }
        }

        pair = default;
        return false;
    }
}