using System.Globalization;
using Lua.Internal;
using Lua.IO;
using Lua.Runtime;

// ReSharper disable MethodHasAsyncOverloadWithCancellation

namespace Lua.Standard;

public sealed class BasicLibrary
{
    public static readonly BasicLibrary Instance = new();

    public BasicLibrary()
    {
        Functions =
        [
            new("assert", Assert),
            new("collectgarbage", CollectGarbage),
            new("dofile", DoFile),
            new("error", Error),
            new("getmetatable", GetMetatable),
            new("ipairs", IPairs),
            new("loadfile", LoadFile),
            new("load", Load),
            new("next", Next),
            new("pairs", Pairs),
            new("pcall", PCall),
            new("print", Print),
            new("rawequal", RawEqual),
            new("rawget", RawGet),
            new("rawlen", RawLen),
            new("rawset", RawSet),
            new("select", Select),
            new("setmetatable", SetMetatable),
            new("tonumber", ToNumber),
            new("tostring", ToString),
            new("type", Type),
            new("xpcall", XPCall),
        ];

        IPairsIterator = new("iterator", (context, cancellationToken) =>
        {
            var table = context.GetArgument<LuaTable>(0);
            var i = context.GetArgument<double>(1);

            i++;
            if (table.TryGetValue(i, out var value))
            {
                return new(context.Return(i, value));
            }
            else
            {
                return new(context.Return(LuaValue.Nil, LuaValue.Nil));
            }
        });

        PairsIterator = new("iterator", Next);
    }

    public readonly LuaFunction[] Functions;
    readonly LuaFunction IPairsIterator;
    readonly LuaFunction PairsIterator;

    public ValueTask<int> Assert(LuaFunctionExecutionContext context, CancellationToken cancellationToken)
    {
        var arg0 = context.GetArgument(0);

        if (!arg0.ToBoolean())
        {
            var message = "assertion failed!";
            if (context.HasArgument(1))
            {
                message = context.GetArgument<string>(1);
            }

            throw new LuaAssertionException(context.Thread, message);
        }

        return new(context.Return(context.Arguments));
    }

    public ValueTask<int> CollectGarbage(LuaFunctionExecutionContext context, CancellationToken cancellationToken)
    {
        if (context.HasArgument(0)) context.GetArgument<string>(0);
        GC.Collect();
        return new(context.Return());
    }

    public async ValueTask<int> DoFile(LuaFunctionExecutionContext context, CancellationToken cancellationToken)
    {
        var arg0 = context.GetArgument<string>(0);
        context.Thread.Stack.PopUntil(context.ReturnFrameBase);
        var closure = await context.State.LoadFileAsync(arg0, "bt", null, cancellationToken);
        return await context.Access.RunAsync(closure, cancellationToken);
    }

    public ValueTask<int> Error(LuaFunctionExecutionContext context, CancellationToken cancellationToken)
    {
        var value = context.ArgumentCount == 0
            ? LuaValue.Nil
            : context.Arguments[0];
        var level = context.HasArgument(1)
            ? context.GetArgument<int>(1)
            : 1;

        throw new LuaRuntimeException(context.Thread, value, level);
    }

    public ValueTask<int> GetMetatable(LuaFunctionExecutionContext context, CancellationToken cancellationToken)
    {
        var arg0 = context.GetArgument(0);

        if (context.State.TryGetMetatable(arg0, out var metatable))
        {
            if (metatable.TryGetValue(Metamethods.Metatable, out var metaMetatable))
            {
                context.Return(metaMetatable);
            }
            else
            {
                context.Return(metatable);
            }
        }
        else
        {
            context.Return(LuaValue.Nil);
        }

        return default;
    }

    public async ValueTask<int> IPairs(LuaFunctionExecutionContext context, CancellationToken cancellationToken)
    {
        var arg0 = context.GetArgument<LuaTable>(0);

        // If table has a metamethod __ipairs, calls it with table as argument and returns the first three results from the call.
        if (arg0.Metatable != null && arg0.Metatable.TryGetValue(Metamethods.IPairs, out var metamethod))
        {
            var stack = context.Thread.Stack;
            var top = stack.Count;
            stack.Push(metamethod);
            stack.Push(arg0);

            await LuaVirtualMachine.Call(context.Access.Thread, top, context.ReturnFrameBase, cancellationToken);
            stack.SetTop(context.ReturnFrameBase + 3);
            return 3;
        }

        return context.Return(IPairsIterator, arg0, 0);
    }

    public async ValueTask<int> LoadFile(LuaFunctionExecutionContext context, CancellationToken cancellationToken)
    {
        var arg0 = context.GetArgument<string>(0);
        var mode = context.HasArgument(1)
            ? context.GetArgument<string>(1)
            : "bt";
        var arg2 = context.HasArgument(2)
            ? context.GetArgument<LuaTable>(2)
            : null;

        // do not use LuaState.DoFileAsync as it uses the newExecutionContext
        try
        {
            return context.Return(await context.State.LoadFileAsync(arg0, mode, arg2, cancellationToken));
        }
        catch (Exception ex)
        {
            return context.Return(LuaValue.Nil, ex.Message);
        }
    }

    public ValueTask<int> Load(LuaFunctionExecutionContext context, CancellationToken cancellationToken)
    {
        // Lua-CSharp does not support binary chunks, the mode argument is ignored.
        var arg0 = context.GetArgument(0);

        var name = context.HasArgument(1)
            ? context.GetArgument<string>(1)
            : null;

        var mode = context.HasArgument(2)
            ? context.GetArgument<string>(2)
            : "bt";

        var arg3 = context.HasArgument(3)
            ? context.GetArgument<LuaTable>(3)
            : null;

        // do not use LuaState.DoFileAsync as it uses the newExecutionContext
        try
        {
            if (arg0.TryRead<string>(out var str))
            {
                return new(context.Return(context.State.Load(str, name ?? str, arg3)));
            }
            else if (arg0.TryRead<LuaFunction>(out var function))
            {
                // TODO: 
                throw new NotImplementedException();
            }
            else if (arg0.TryRead<IBinaryData>(out var binaryData))
            {
                return new(context.Return(context.State.Load(binaryData.Memory.Span, name, "bt", arg3)));
            }
            else
            {
                LuaRuntimeException.BadArgument(context.Thread, 1, ["string", "function,binary data"], arg0.TypeToString());
                return default; // dummy
            }
        }
        catch (Exception ex)
        {
            return new(context.Return(LuaValue.Nil, ex.Message));
        }
    }

    public ValueTask<int> Next(LuaFunctionExecutionContext context, CancellationToken cancellationToken)
    {
        var arg0 = context.GetArgument<LuaTable>(0);
        var arg1 = context.HasArgument(1) ? context.Arguments[1] : LuaValue.Nil;

        if (arg0.TryGetNext(arg1, out var kv))
        {
            return new(context.Return(kv.Key, kv.Value));
        }
        else
        {
            return new(context.Return(LuaValue.Nil));
        }
    }

    public async ValueTask<int> Pairs(LuaFunctionExecutionContext context, CancellationToken cancellationToken)
    {
        var arg0 = context.GetArgument<LuaTable>(0);

        // If table has a metamethod __pairs, calls it with table as argument and returns the first three results from the call.
        if (arg0.Metatable != null && arg0.Metatable.TryGetValue(Metamethods.Pairs, out var metamethod))
        {
            var stack = context.Thread.Stack;
            var top = stack.Count;
            stack.Push(metamethod);
            stack.Push(arg0);

            await LuaVirtualMachine.Call(context.Access.Thread, top, context.ReturnFrameBase, cancellationToken);
            stack.SetTop(context.ReturnFrameBase + 3);
            return 3;
        }

        return (context.Return(PairsIterator, arg0, LuaValue.Nil));
    }

    public async ValueTask<int> PCall(LuaFunctionExecutionContext context, CancellationToken cancellationToken)
    {
        var frameCount = context.Thread.CallStackFrameCount;
        try
        {
            var count = await LuaVirtualMachine.Call(context.Access.Thread, context.FrameBase, context.ReturnFrameBase + 1, cancellationToken);

            context.Thread.Stack.Get(context.ReturnFrameBase) = true;
            return count + 1;
        }
        catch (Exception ex)
        {
            context.Thread.PopCallStackFrameUntil(frameCount);
            switch (ex)
            {
                case LuaCanceledException:
                    throw;
                case OperationCanceledException:
                    throw new LuaCanceledException(context.Thread, cancellationToken, ex);
                case LuaRuntimeException luaEx:
                    {
                        if (luaEx.InnerException == null && luaEx.ErrorObject.Type != LuaValueType.String)
                        {
                            return context.Return(false, luaEx.ErrorObject);
                        }

                        using var builder = new PooledList<char>();
                        var message = luaEx.MinimalMessage();
                        luaEx.Forget();
                        return context.Return(false, message);
                    }
                default:
                    return context.Return(false, ex.Message);
            }
        }
    }

    public async ValueTask<int> Print(LuaFunctionExecutionContext context, CancellationToken cancellationToken)
    {
        for (int i = 0; i < context.ArgumentCount; i++)
        {
            await context.Arguments[i].CallToStringAsync(context, cancellationToken);
            Console.Write(context.Thread.Stack.Pop().Read<string>());
            Console.Write('\t');
        }

        Console.WriteLine();
        return context.Return();
    }

    public ValueTask<int> RawEqual(LuaFunctionExecutionContext context, CancellationToken cancellationToken)
    {
        var arg0 = context.GetArgument(0);
        var arg1 = context.GetArgument(1);

        return new(context.Return(arg0 == arg1));
    }

    public ValueTask<int> RawGet(LuaFunctionExecutionContext context, CancellationToken cancellationToken)
    {
        var arg0 = context.GetArgument<LuaTable>(0);
        var arg1 = context.GetArgument(1);
        return new(context.Return(arg0[arg1]));
    }

    public ValueTask<int> RawLen(LuaFunctionExecutionContext context, CancellationToken cancellationToken)
    {
        var arg0 = context.GetArgument(0);

        if (arg0.TryRead<LuaTable>(out var table))
        {
            return new(context.Return(table.ArrayLength));
        }
        else if (arg0.TryRead<string>(out var str))
        {
            return new(context.Return(str.Length));
        }
        else
        {
            LuaRuntimeException.BadArgument(context.Thread, 2, [LuaValueType.String, LuaValueType.Table], arg0.Type);
            return default;
        }
    }

    public ValueTask<int> RawSet(LuaFunctionExecutionContext context, CancellationToken cancellationToken)
    {
        var arg0 = context.GetArgument<LuaTable>(0);
        var arg1 = context.GetArgument(1);
        var arg2 = context.GetArgument(2);

        arg0[arg1] = arg2;
        return new(context.Return());
    }

    public ValueTask<int> Select(LuaFunctionExecutionContext context, CancellationToken cancellationToken)
    {
        var arg0 = context.GetArgument(0);

        if (arg0.TryRead<int>(out var index))
        {
            if (Math.Abs(index) > context.ArgumentCount)
            {
                throw new LuaRuntimeException(context.Thread, "bad argument #1 to 'select' (index out of range)");
            }

            var span = index >= 0
                ? context.Arguments[index..]
                : context.Arguments[(context.ArgumentCount + index)..];

            return new(context.Return(span));
        }
        else if (arg0.TryRead<string>(out var str) && str == "#")
        {
            return new(context.Return(context.ArgumentCount - 1));
        }
        else
        {
            LuaRuntimeException.BadArgument(context.Thread, 1, LuaValueType.Number, arg0.Type);
            return default;
        }
    }

    public ValueTask<int> SetMetatable(LuaFunctionExecutionContext context, CancellationToken cancellationToken)
    {
        var arg0 = context.GetArgument<LuaTable>(0);
        var arg1 = context.GetArgument(1);

        if (arg1.Type is not (LuaValueType.Nil or LuaValueType.Table))
        {
            LuaRuntimeException.BadArgument(context.Thread, 2, [LuaValueType.Nil, LuaValueType.Table], arg1.Type);
        }

        if (arg0.Metatable != null && arg0.Metatable.TryGetValue(Metamethods.Metatable, out _))
        {
            throw new LuaRuntimeException(context.Thread, "cannot change a protected metatable");
        }
        else if (arg1.Type is LuaValueType.Nil)
        {
            arg0.Metatable = null;
        }
        else
        {
            arg0.Metatable = arg1.Read<LuaTable>();
        }


        return new(context.Return(arg0));
    }

    public ValueTask<int> ToNumber(LuaFunctionExecutionContext context, CancellationToken cancellationToken)
    {
        var e = context.GetArgument(0);
        int? toBase = context.HasArgument(1)
            ? (int)context.GetArgument<double>(1)
            : null;

        if (toBase != null && (toBase < 2 || toBase > 36))
        {
            throw new LuaRuntimeException(context.Thread, "bad argument #2 to 'tonumber' (base out of range)");
        }

        double? value = null;
        if (e.Type is LuaValueType.Number)
        {
            value = e.UnsafeRead<double>();
        }
        else if (e.TryRead<string>(out var str))
        {
            if (toBase == null)
            {
                if (e.TryRead<double>(out var result))
                {
                    value = result;
                }
            }
            else if (toBase == 10)
            {
                if (double.TryParse(str, NumberStyles.Float, CultureInfo.InvariantCulture, out var result))
                {
                    value = result;
                }
            }
            else
            {
                try
                {
                    // if the base is not 10, str cannot contain a minus sign
                    var span = str.AsSpan().Trim();
                    if (span.Length == 0) goto END;

                    var first = span[0];
                    var sign = first == '-' ? -1 : 1;
                    if (first is '+' or '-')
                    {
                        span = span[1..];
                    }

                    if (span.Length == 0) goto END;

                    if (toBase == 16 && span.Length > 2 && span[0] is '0' && span[1] is 'x' or 'X')
                    {
                        value = sign * HexConverter.ToDouble(span);
                    }
                    else
                    {
                        value = sign * StringToDouble(span, toBase.Value);
                    }
                }
                catch (FormatException)
                {
                    goto END;
                }
            }
        }
        else
        {
            goto END;
        }

    END:
        if (value is double.NaN)
        {
            value = null;
        }


        return new(context.Return(value ?? LuaValue.Nil));
    }

    static double StringToDouble(ReadOnlySpan<char> text, int toBase)
    {
        var value = 0.0;
        for (int i = 0; i < text.Length; i++)
        {
            var v = text[i] switch
            {
                '0' => 0,
                '1' => 1,
                '2' => 2,
                '3' => 3,
                '4' => 4,
                '5' => 5,
                '6' => 6,
                '7' => 7,
                '8' => 8,
                '9' => 9,
                'a' or 'A' => 10,
                'b' or 'B' => 11,
                'c' or 'C' => 12,
                'd' or 'D' => 13,
                'e' or 'E' => 14,
                'f' or 'F' => 15,
                'g' or 'G' => 16,
                'h' or 'H' => 17,
                'i' or 'I' => 18,
                'j' or 'J' => 19,
                'k' or 'K' => 20,
                'l' or 'L' => 21,
                'm' or 'M' => 22,
                'n' or 'N' => 23,
                'o' or 'O' => 24,
                'p' or 'P' => 25,
                'q' or 'Q' => 26,
                'r' or 'R' => 27,
                's' or 'S' => 28,
                't' or 'T' => 29,
                'u' or 'U' => 30,
                'v' or 'V' => 31,
                'w' or 'W' => 32,
                'x' or 'X' => 33,
                'y' or 'Y' => 34,
                'z' or 'Z' => 35,
                _ => 0,
            };

            if (v >= toBase)
            {
                throw new FormatException();
            }

            value += v * Math.Pow(toBase, text.Length - i - 1);
        }

        return value;
    }

    public ValueTask<int> ToString(LuaFunctionExecutionContext context, CancellationToken cancellationToken)
    {
        var arg0 = context.GetArgument(0);
        context.Return();
        return arg0.CallToStringAsync(context, cancellationToken);
    }

    public ValueTask<int> Type(LuaFunctionExecutionContext context, CancellationToken cancellationToken)
    {
        var arg0 = context.GetArgument(0);

        return new(context.Return(arg0.Type switch
        {
            LuaValueType.Nil => "nil",
            LuaValueType.Boolean => "boolean",
            LuaValueType.String => "string",
            LuaValueType.Number => "number",
            LuaValueType.Function => "function",
            LuaValueType.Thread => "thread",
            LuaValueType.LightUserData => "userdata",
            LuaValueType.UserData => "userdata",
            LuaValueType.Table => "table",
            _ => throw new NotImplementedException(),
        }));
    }

    public async ValueTask<int> XPCall(LuaFunctionExecutionContext context, CancellationToken cancellationToken)
    {
        var frameCount = context.Thread.CallStackFrameCount;
        var arg0 = context.GetArgument(0);
        var arg1 = context.GetArgument<LuaFunction>(1);

        try
        {
            var stack = context.Thread.Stack;
            stack.Get(context.FrameBase + 1) = arg0;
            var count = await LuaVirtualMachine.Call(context.Access.Thread, context.FrameBase + 1, context.ReturnFrameBase + 1, cancellationToken);

            context.Thread.Stack.Get(context.ReturnFrameBase) = true;
            return count + 1;
        }
        catch (Exception ex)
        {
            var thread = context.Thread;
            thread.PopCallStackFrameUntil(frameCount);
            cancellationToken.ThrowIfCancellationRequested();

            var access = thread.CurrentAccess;
            if (ex is LuaRuntimeException luaEx)
            {
                luaEx.Forget();
                access.Push(luaEx.ErrorObject);
            }
            else
            {
                access.Push(ex.Message);
            }


            // invoke error handler
            var count = await access.RunAsync(arg1, 1, context.ReturnFrameBase + 1, cancellationToken);
            context.Thread.Stack.Get(context.ReturnFrameBase) = false;
            return count + 1;
        }
    }
}