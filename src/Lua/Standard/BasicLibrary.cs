using System.Globalization;
using System.Runtime.CompilerServices;
using Lua.CodeAnalysis.Compilation;
using Lua.Internal;
using Lua.Internal.CompilerServices;
using Lua.IO;
using Lua.Runtime;

// ReSharper disable MethodHasAsyncOverloadWithCancellation

namespace Lua.Standard;

public sealed class BasicLibrary
{
    public static readonly BasicLibrary Instance = new();

    static readonly HashSet<string> KnownCollectGarbageOptions = new(StringComparer.Ordinal)
    {
        "collect",
        "stop",
        "restart",
        "count",
        "step",
        "setpause",
        "setstepmul",
        "setmajorinc",
        "isrunning",
        "incremental",
        "generational",
    };

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

        IPairsIterator = new(
            "iterator",
            (context, cancellationToken) =>
            {
                var table = context.GetArgument<LuaTable>(0);
                var i = Math.Truncate(context.GetArgument<double>(1));

                i++;
                if (table.TryGetValue(i, out var value))
                {
                    return new(context.Return(i, value));
                }
                else
                {
                    return new(context.Return(LuaValue.Nil));
                }
            }
        );

        PairsIterator = new("iterator", Next);
    }

    public readonly LuaFunction[] Functions;
    readonly LuaFunction IPairsIterator;
    readonly LuaFunction PairsIterator;

    public ValueTask<int> Assert(
        LuaFunctionExecutionContext context,
        CancellationToken cancellationToken
    )
    {
        var arg0 = context.GetArgument(0);

        if (!arg0.ToBoolean())
        {
            var message = "assertion failed!";
            if (context.HasArgument(1))
            {
                message = context.GetStringArgument(1);
            }

            throw new LuaAssertionException(context.State, message);
        }

        return new(context.Return(context.Arguments));
    }

    public ValueTask<int> CollectGarbage(
        LuaFunctionExecutionContext context,
        CancellationToken cancellationToken
    )
    {
        if (context.HasArgument(0))
        {
            var option = context.GetStringArgument(0);
            if (!KnownCollectGarbageOptions.Contains(option))
            {
                throw new LuaRuntimeException(
                    context.State,
                    $"bad argument #1 to 'collectgarbage' (invalid option '{option}')"
                );
            }
        }

        GC.Collect();
        return new(context.Return());
    }

    [AsyncMethodBuilder(typeof(LightAsyncValueTaskMethodBuilder<>))]
    public async ValueTask<int> DoFile(
        LuaFunctionExecutionContext context,
        CancellationToken cancellationToken
    )
    {
        var arg0 = context.GetStringArgument(0);
        context.State.Stack.PopUntil(context.ReturnFrameBase);
        var closure = await context.State.LoadFileAsync(
            context.State.Platform.DecodeText(arg0),
            "bt",
            null,
            cancellationToken
        );
        return await context.State.RunCoreAsync(
            closure,
            0,
            context.State.Stack.Count,
            cancellationToken
        );
    }

    public ValueTask<int> Error(
        LuaFunctionExecutionContext context,
        CancellationToken cancellationToken
    )
    {
        var value = context.ArgumentCount == 0 ? LuaValue.Nil : context.Arguments[0];
        var level = context.HasArgument(1) ? context.GetIntegerArgument(1) : 1;
        if (level > 0 && value.Type == LuaValueType.Number)
            value = value.ToLuaString(context.State.Platform.NumberFormat);

        throw new LuaRuntimeException(context.State, value, level);
    }

    public ValueTask<int> GetMetatable(
        LuaFunctionExecutionContext context,
        CancellationToken cancellationToken
    )
    {
        var arg0 = context.GetArgument(0);

        if (context.GlobalState.TryGetMetatable(arg0, out var metatable))
        {
            if (metatable.TryGetValue(Metamethods.Metatable, out var metaMetatable))
            {
                return new(context.Return(metaMetatable));
            }
            else
            {
                return new(context.Return(metatable));
            }
        }
        else
        {
            return new(context.Return(LuaValue.Nil));
        }
    }

    [AsyncMethodBuilder(typeof(LightAsyncValueTaskMethodBuilder<>))]
    public async ValueTask<int> IPairs(
        LuaFunctionExecutionContext context,
        CancellationToken cancellationToken
    )
    {
        var arg0 = context.GetArgument(0);

        // If table has a metamethod __ipairs, calls it with table as argument and returns the first three results from the call.
        if (
            context.State.GlobalState.TryGetMetatable(arg0, out var metaTable)
            && metaTable.TryGetValue(Metamethods.IPairs, out var metamethod)
        )
        {
            var stack = context.State.Stack;
            var top = stack.Count;
            stack.Push(metamethod);
            stack.Push(arg0);

            context.State.NonYieldableCallCount++;
            try
            {
                await LuaVirtualMachine.Call(
                    context.State,
                    top,
                    context.ReturnFrameBase,
                    cancellationToken
                );
            }
            finally
            {
                context.State.NonYieldableCallCount--;
            }
            stack.SetTop(context.ReturnFrameBase + 3);
            return 3;
        }

        if (arg0.Type != LuaValueType.Table)
        {
            LuaRuntimeException.BadArgument(context.State, 1, LuaValueType.Table, arg0.Type);
        }

        return context.Return(IPairsIterator, arg0, 0);
    }

    public async ValueTask<int> LoadFile(
        LuaFunctionExecutionContext context,
        CancellationToken cancellationToken
    )
    {
        var arg0 = context.GetStringArgument(0);
        var mode = context.HasArgument(1) ? context.GetStringArgument(1) : "bt";
        var hasEnvironment = context.ArgumentCount > 2;
        var environment = hasEnvironment ? context.GetArgument(2) : LuaValue.Nil;

        // do not use LuaState.DoFileAsync as it uses the newExecutionContext
        try
        {
            var closure = await context.State.LoadFileAsync(
                context.State.Platform.DecodeText(arg0),
                mode,
                null,
                cancellationToken
            );
            if (hasEnvironment)
            {
                closure.SetEnvironment(environment);
            }

            return context.Return(closure);
        }
        catch (Exception ex)
        {
            return context.Return(LuaValue.Nil, context.State.Platform.GetErrorMessage(ex));
        }
    }

    [AsyncMethodBuilder(typeof(LightAsyncValueTaskMethodBuilder<>))]
    public async ValueTask<int> Load(
        LuaFunctionExecutionContext context,
        CancellationToken cancellationToken
    )
    {
        var arg0 = context.GetArgument(0);

        var name = context.HasArgument(1) ? context.GetStringArgument(1) : null;

        var mode = context.HasArgument(2) ? context.GetStringArgument(2) : "bt";

        var hasEnvironment = context.ArgumentCount > 3;
        var environment = hasEnvironment ? context.GetArgument(3) : LuaValue.Nil;
        if (arg0.Type is not (LuaValueType.String or LuaValueType.Number or LuaValueType.Function))
            context.ThrowBadArgument(1, "function expected");
        var previousHandler = context.State.ErrorHandlerIndex;
        context.State.ErrorHandlerIndex = 0;

        // do not use LuaState.DoFileAsync as it uses the newExecutionContext
        try
        {
            LuaClosure closure;
            if (arg0.Type is LuaValueType.String or LuaValueType.Number)
            {
                var source = context.GetStringArgument(0);
                closure = LoadSource(context.State, source, name ?? source, mode);
            }
            else
            {
                var function = arg0.Read<LuaFunction>();
                using var source = new PooledList<char>(256);
                var stack = context.State.Stack;
                var top = stack.Count;
                context.State.NonYieldableCallCount++;
                try
                {
                    while (true)
                    {
                        var count = await context.State.RunCoreAsync(
                            function,
                            0,
                            top,
                            cancellationToken
                        );
                        var piece = count == 0 ? LuaValue.Nil : stack.Get(top);
                        stack.PopUntil(top);
                        if (piece.Type == LuaValueType.Nil)
                            break;
                        if (piece.Type is not (LuaValueType.String or LuaValueType.Number))
                            throw new LuaRuntimeException(
                                context.State,
                                "reader function must return a string"
                            );
                        var text = piece.ToLuaString(context.State.Platform.NumberFormat);
                        if (text.Length == 0)
                            break;
                        if (source.Count == 0)
                            CheckLoadMode(text[0] == '\e', mode);
                        source.AddRange(text);
                    }
                }
                finally
                {
                    context.State.NonYieldableCallCount--;
                }
                closure = LoadSource(context.State, source.AsSpan(), name ?? "=(load)", mode);
            }
            if (hasEnvironment)
                closure.SetEnvironment(environment);
            return context.Return(closure);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            if (ex is LuaRuntimeException luaException)
            {
                var error =
                    luaException.InnerException == null
                    && luaException.ErrorObject.Type != LuaValueType.String
                        ? luaException.ErrorObject
                        : luaException.MinimalMessage(context.State.Platform);
                luaException.Forget();
                return context.Return(LuaValue.Nil, error);
            }
            return context.Return(LuaValue.Nil, context.State.Platform.GetErrorMessage(ex));
        }
        finally
        {
            context.State.ErrorHandlerIndex = previousHandler;
        }
    }

    static void CheckLoadMode(bool binary, string mode)
    {
        if (!mode.Contains(binary ? 'b' : 't'))
            throw new Exception(
                $"attempt to load a {(binary ? "binary" : "text")} chunk (mode is '{mode}')"
            );
    }

    static LuaClosure LoadSource(
        LuaState state,
        ReadOnlySpan<char> source,
        string name,
        string mode
    )
    {
        CheckLoadMode(!source.IsEmpty && source[0] == '\e', mode);
        if (!source.IsEmpty && source[0] == '\e')
        {
            using PooledArray<byte> bytes = new(source.Length);
            LuaByteEncoding.Instance.GetBytes(source, bytes.AsSpan());
            return new(state, Parser.Undump(bytes.AsSpan(), name, state.Platform.StringMode));
        }
        return state.LoadCore(source, name);
    }

    public ValueTask<int> Next(
        LuaFunctionExecutionContext context,
        CancellationToken cancellationToken
    )
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

    [AsyncMethodBuilder(typeof(LightAsyncValueTaskMethodBuilder<>))]
    public async ValueTask<int> Pairs(
        LuaFunctionExecutionContext context,
        CancellationToken cancellationToken
    )
    {
        var arg0 = context.GetArgument(0);

        // If table has a metamethod __pairs, calls it with table as argument and returns the first three results from the call.
        if (
            context.State.GlobalState.TryGetMetatable(arg0, out var metaTable)
            && metaTable.TryGetValue(Metamethods.Pairs, out var metamethod)
        )
        {
            var stack = context.State.Stack;
            var top = stack.Count;
            stack.Push(metamethod);
            stack.Push(arg0);

            context.State.NonYieldableCallCount++;
            try
            {
                await LuaVirtualMachine.Call(
                    context.State,
                    top,
                    context.ReturnFrameBase,
                    cancellationToken
                );
            }
            finally
            {
                context.State.NonYieldableCallCount--;
            }
            stack.SetTop(context.ReturnFrameBase + 3);
            return 3;
        }

        if (arg0.Type != LuaValueType.Table)
        {
            LuaRuntimeException.BadArgument(context.State, 1, LuaValueType.Table, arg0.Type);
        }

        return context.Return(PairsIterator, arg0, LuaValue.Nil);
    }

    [AsyncMethodBuilder(typeof(LightAsyncValueTaskMethodBuilder<>))]
    public async ValueTask<int> PCall(
        LuaFunctionExecutionContext context,
        CancellationToken cancellationToken
    )
    {
        context.GetArgument(0);
        var frameCount = context.State.CallStackFrameCount;
        var previousHandler = context.State.ErrorHandlerIndex;
        context.State.ErrorHandlerIndex = 0;
        try
        {
            var count = await LuaVirtualMachine.Call(
                context.State,
                context.FrameBase,
                context.ReturnFrameBase + 1,
                cancellationToken
            );

            context.State.Stack.Get(context.ReturnFrameBase) = true;
            return count + 1;
        }
        catch (Exception ex)
        {
            context.State.PopCallStackFrameUntil(frameCount);
            switch (ex)
            {
                case LuaCanceledException:
                    throw;
                case OperationCanceledException:
                    throw new LuaCanceledException(context.State, cancellationToken, ex);
                case LuaRuntimeException luaEx:
                {
                    LuaValue error =
                        luaEx.InnerException == null
                        && luaEx.ErrorObject.Type != LuaValueType.String
                            ? luaEx.ErrorObject
                            : luaEx.MinimalMessage(context.State.Platform);
                    luaEx.Forget();
                    return context.Return(false, error);
                }
                default:
                    return context.Return(false, context.State.Platform.GetErrorMessage(ex));
            }
        }
        finally
        {
            context.State.ErrorHandlerIndex = previousHandler;
        }
    }

    [AsyncMethodBuilder(typeof(LightAsyncValueTaskMethodBuilder<>))]
    public async ValueTask<int> Print(
        LuaFunctionExecutionContext context,
        CancellationToken cancellationToken
    )
    {
        var stdout = context.GlobalState.Platform.StandardIO.Output;
        var argumentBase = context.FrameBase;
        var environment = context.State.Environment;
        context.State.NonYieldableCallCount++;
        try
        {
            if (!environment.TryGetValue("tostring", out var toString))
                toString = await LuaVirtualMachine.ExecuteGetTableSlowPath(
                    context.State,
                    environment,
                    "tostring",
                    cancellationToken
                );

            for (var i = 0; i < context.ArgumentCount; i++)
            {
                var stack = context.State.Stack;
                var top = stack.Count;
                stack.PushRange(toString, stack.Get(argumentBase + i));
                var count = await LuaVirtualMachine.Call(
                    context.State,
                    top,
                    top,
                    cancellationToken
                );
                var value = count > 0 ? stack.Get(top) : LuaValue.Nil;
                if (value.Type is not (LuaValueType.String or LuaValueType.Number))
                    throw new LuaRuntimeException(
                        context.State,
                        "'tostring' must return a string to 'print'"
                    );
                var text = value.ToLuaString(context.State.Platform.NumberFormat);
                stack.PopUntil(top);
                await stdout.WriteAsync(text, cancellationToken);
                if (i < context.ArgumentCount - 1)
                {
                    await stdout.WriteAsync("\t", cancellationToken);
                }
            }

            await stdout.WriteAsync("\n", cancellationToken);
            await stdout.FlushAsync(cancellationToken);
            return context.Return();
        }
        finally
        {
            context.State.NonYieldableCallCount--;
        }
    }

    public ValueTask<int> RawEqual(
        LuaFunctionExecutionContext context,
        CancellationToken cancellationToken
    )
    {
        var arg0 = context.GetArgument(0);
        var arg1 = context.GetArgument(1);

        return new(context.Return(arg0 == arg1));
    }

    public ValueTask<int> RawGet(
        LuaFunctionExecutionContext context,
        CancellationToken cancellationToken
    )
    {
        var arg0 = context.GetArgument<LuaTable>(0);
        var arg1 = context.GetArgument(1);
        return new(context.Return(arg0[arg1]));
    }

    public ValueTask<int> RawLen(
        LuaFunctionExecutionContext context,
        CancellationToken cancellationToken
    )
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
            LuaRuntimeException.BadArgument(
                context.State,
                2,
                [LuaValueType.String, LuaValueType.Table],
                arg0.Type
            );
            return default;
        }
    }

    public ValueTask<int> RawSet(
        LuaFunctionExecutionContext context,
        CancellationToken cancellationToken
    )
    {
        var arg0 = context.GetArgument<LuaTable>(0);
        var arg1 = context.GetArgument(1);
        var arg2 = context.GetArgument(2);

        arg0[arg1] = arg2;
        return new(context.Return(arg0));
    }

    public ValueTask<int> Select(
        LuaFunctionExecutionContext context,
        CancellationToken cancellationToken
    )
    {
        var selector = context.GetArgument(0);
        if (selector.Type == LuaValueType.String)
        {
            var text = selector.UnsafeReadString();
            if (text.Length > 0 && text[0] == '#')
                return new(context.Return(context.ArgumentCount - 1));
        }

        var index = Math.Truncate(context.GetArgument<double>(0));
        if (!MathEx.IsFinite(index))
            context.ThrowBadArgument(1, "index out of range");
        if (index < 0)
            index += context.ArgumentCount;
        else if (index > context.ArgumentCount)
            index = context.ArgumentCount;
        if (index < 1)
            context.ThrowBadArgument(1, "index out of range");
        return new(context.Return(context.Arguments[(int)index..]));
    }

    public ValueTask<int> SetMetatable(
        LuaFunctionExecutionContext context,
        CancellationToken cancellationToken
    )
    {
        var arg0 = context.GetArgument<LuaTable>(0);
        var arg1 = context.GetArgument(1);

        if (arg1.Type is not (LuaValueType.Nil or LuaValueType.Table))
        {
            LuaRuntimeException.BadArgument(
                context.State,
                2,
                [LuaValueType.Nil, LuaValueType.Table],
                arg1.Type
            );
        }

        if (arg0.Metatable != null && arg0.Metatable.TryGetValue(Metamethods.Metatable, out _))
        {
            throw new LuaRuntimeException(context.State, "cannot change a protected metatable");
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

    public ValueTask<int> ToNumber(
        LuaFunctionExecutionContext context,
        CancellationToken cancellationToken
    )
    {
        var value = context.GetArgument(0);
        if (!context.HasArgument(1))
        {
            if (value.Type == LuaValueType.Number)
                return new(context.Return(value));
            return new(
                context.Return(
                    value.TryReadDouble(out var number) && !double.IsNaN(number)
                        ? new LuaValue(number)
                        : LuaValue.Nil
                )
            );
        }

        // Lua coerces numeric inputs to text before interpreting an explicit base.
        string text;
        if (value.Type == LuaValueType.String)
            text = value.UnsafeReadString();
        else if (value.Type == LuaValueType.Number)
            text = value.ToLuaString(context.State.Platform.NumberFormat);
        else
        {
            LuaRuntimeException.BadArgument(context.State, 1, LuaValueType.String, value.Type);
            return default;
        }

        var toBase = (int)context.GetArgument<double>(1);
        if (toBase is < 2 or > 36)
            context.ThrowBadArgument(2, "base out of range");
        var parsed = StringToDouble(text.AsSpan(), toBase);
        return new(context.Return(parsed.HasValue ? new LuaValue(parsed.Value) : LuaValue.Nil));
    }

    static double? StringToDouble(ReadOnlySpan<char> text, int toBase)
    {
        text = text.Trim(" \f\n\r\t\v");
        if (text.IsEmpty)
            return null;
        var negative = text[0] == '-';
        if (text[0] is '+' or '-')
            text = text[1..];
        if (text.IsEmpty)
            return null;

        double value = 0;
        foreach (var c in text)
        {
            var digit = c switch
            {
                >= '0' and <= '9' => c - '0',
                >= 'A' and <= 'Z' => c - 'A' + 10,
                >= 'a' and <= 'z' => c - 'a' + 10,
                _ => 36,
            };
            if (digit >= toBase)
                return null;
            value = value * toBase + digit;
        }
        return negative ? -value : value;
    }

    public ValueTask<int> ToString(
        LuaFunctionExecutionContext context,
        CancellationToken cancellationToken
    )
    {
        var arg0 = context.GetArgument(0);
        context.Return();
        return arg0.CallToStringAsync(context, cancellationToken);
    }

    public ValueTask<int> Type(
        LuaFunctionExecutionContext context,
        CancellationToken cancellationToken
    )
    {
        var arg0 = context.GetArgument(0);

        return new(
            context.Return(
                arg0.Type switch
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
                }
            )
        );
    }

    [AsyncMethodBuilder(typeof(LightAsyncValueTaskMethodBuilder<>))]
    public async ValueTask<int> XPCall(
        LuaFunctionExecutionContext context,
        CancellationToken cancellationToken
    )
    {
        var state = context.State;
        var frameCount = state.CallStackFrameCount;
        var arg0 = context.GetArgument(0);
        var arg1 = context.GetArgument(1);
        var previousHandler = state.ErrorHandlerIndex;
        var functionBase = context.FrameBase + 1;
        // Keep the handler below the forwarded arguments and the result range.
        var handlerIndex = Math.Min(context.ReturnFrameBase, context.FrameBase);
        state.Stack.Get(handlerIndex) = arg1;
        state.ErrorHandlerIndex = handlerIndex + 1;

        try
        {
            var stack = context.State.Stack;
            stack.Get(functionBase) = arg0;
            var count = await LuaVirtualMachine.Call(
                context.State,
                functionBase,
                context.ReturnFrameBase + 1,
                cancellationToken
            );

            stack.Get(context.ReturnFrameBase) = true;
            return count + 1;
        }
        catch (Exception ex)
            when (state.ErrorHandlerIndex != 0 && ex is not OperationCanceledException)
        {
            ex = await state.HandleErrorAsync(ex, cancellationToken);
            return FinishXPCall(context, ex, frameCount, handlerIndex, cancellationToken);
        }
        catch (Exception ex)
        {
            return FinishXPCall(context, ex, frameCount, handlerIndex, cancellationToken);
        }
        finally
        {
            state.ErrorHandlerIndex = previousHandler;
        }
    }

    static int FinishXPCall(
        LuaFunctionExecutionContext context,
        Exception exception,
        int frameCount,
        int handlerIndex,
        CancellationToken cancellationToken
    )
    {
        context.State.PopCallStackFrameUntil(frameCount);
        if (exception is LuaCanceledException)
            throw exception;
        if (exception is OperationCanceledException)
            throw new LuaCanceledException(context.State, cancellationToken, exception);
        if (exception is LuaRuntimeException luaException)
            luaException.Forget();
        return context.Return(false, context.State.Stack.Get(handlerIndex));
    }
}
