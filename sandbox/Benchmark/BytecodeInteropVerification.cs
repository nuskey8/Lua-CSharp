using Lua;
using Lua.Platforms;
using Lua.Standard;
using NuLua;
using NuLua.Lua52;
using LuaFunction = Lua.LuaFunction;
using LuaTable = Lua.LuaTable;

static class BytecodeInteropVerification
{
    static readonly ComparisonCase Closures = new(
        "dump-closures",
        "Bytecode",
        """
        local value=10
        local function make(step)
            return function(...)
                value=value+step
                local first=...
                return value+select('#',...)+first
            end
        end
        local a,b=make(3),make(5)
        assert(a(4)==18 and b(7,8)==27)
        return a(20)
        """,
        42
    );

    static readonly ComparisonCase BinaryStrings = new(
        "dump-binary-strings",
        "Bytecode",
        """
        local bytes='\0\128\255'
        local utf8='\225\186\191'
        local function check()
            assert(#bytes==3 and bytes:byte(1)==0 and bytes:byte(2)==128 and bytes:byte(3)==255)
            assert(#utf8==3 and utf8:byte(1)==225 and utf8:byte(2)==186 and utf8:byte(3)==191)
            assert(''==string.sub(bytes,1,0))
            assert(true and not false and nil==nil)
            return 42
        end
        return check()
        """,
        42
    );

    public static async Task VerifyAsync()
    {
        var buffer = new byte[1024 * 1024];
        foreach (
            var workload in ComparisonWorkloads
                .Cases.Concat(ComparisonWorkloads.CompilationCases)
                .Append(Closures)
                .Append(BinaryStrings)
                .DistinctBy(c => c.Name)
        )
        {
            using var native = Lua52State.Create();
            native.OpenLibraries();
            native.RegisterFunction(
                "host_add",
                (vm, args) =>
                {
                    vm.PushNumber(args[0].Read<double>() + args[1].Read<double>());
                    return 1;
                }
            );
            native.LoadString(workload.Source, "@bytecode");
            if (!native.TryDump(1, false, buffer, out var written))
                throw new InvalidOperationException(
                    $"Native dump exceeds verification buffer: {workload.Name}"
                );
            native.SetTop(0);

            foreach (var mode in new[] { LuaStringMode.Utf16, LuaStringMode.Bytes })
            {
                // Arbitrary bytes have the same semantics as C Lua in byte mode.
                if (workload == BinaryStrings && mode != LuaStringMode.Bytes)
                    continue;
                using var state = LuaState.Create(
                    mode == LuaStringMode.Bytes
                        ? LuaPlatform.DefaultByteStrings
                        : LuaPlatform.Default
                );
                state.OpenStandardLibraries();
                state.Environment["host_add"] = new LuaFunction(
                    (context, _) =>
                        new(
                            context.Return(
                                context.GetArgument<double>(0) + context.GetArgument<double>(1)
                            )
                        )
                );

                var loaded = state.Load(buffer.AsSpan(0, written), "native", "b");
                var results = await state.ExecuteAsync(loaded);
                ComparisonWorkloads.Check(
                    workload,
                    $"NuLua dump -> Lua-CSharp {mode}",
                    results.Single().Read<double>()
                );

                var original = state.Load(workload.Source, "@bytecode");
                var dump = state.Environment["string"].Read<LuaTable>()["dump"].Read<LuaFunction>();
                var dumped = (await state.CallAsync(dump, [original])).Single().Read<string>();
                native.LoadString("return assert(load(...,'managed','b'))()", "@bytecode");
                native.PushString(LuaByteString.ToBytes(dumped));
                native.Call(1, 1);
                ComparisonWorkloads.Check(
                    workload,
                    $"Lua-CSharp {mode} dump -> NuLua",
                    native.ToNumber(-1)
                );
                native.SetTop(0);
            }
            Console.WriteLine($"PASS Bytecode     {workload.Name, -20} NuLua <-> Lua-CSharp");
        }
    }
}
