using Lua.Internal;
using Lua.IO;

namespace Lua.Standard.Internal;

static class IOHelper
{
    public static async ValueTask<int> Open(
        LuaState state,
        string fileName,
        string mode,
        bool throwError,
        CancellationToken cancellationToken
    )
    {
        var fileMode = LuaFileOpenModeExtensions.ParseModeFromString(mode);
        if (!fileMode.IsValid())
        {
            throw new LuaRuntimeException(state, "bad argument #2 to 'open' (invalid mode)");
        }

        try
        {
            var stream = await state.GlobalState.Platform.FileSystem.Open(
                state.Platform.DecodeText(fileName),
                fileMode,
                cancellationToken
            );

            state.Stack.Push(new(new FileHandle(stream, state.GlobalState.FileHandleMetatable)));
            return 1;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            if (throwError)
            {
                throw;
            }

            state.Stack.Push(LuaValue.Nil);
            state.Stack.Push(state.Platform.EncodeText(ex.Message));
            state.Stack.Push(ex.HResult);
            return 3;
        }
    }

    // TODO: optimize (use IBuffertWrite<byte>, async)
    public static async ValueTask<int> WriteAsync(
        FileHandle file,
        string name,
        LuaFunctionExecutionContext context,
        CancellationToken cancellationToken
    )
    {
        if (!file.IsOpen)
        {
            throw new LuaRuntimeException(context.State, "attempt to use a closed file");
        }

        try
        {
            for (var i = 0; i < context.ArgumentCount; i++)
            {
                var arg = context.Arguments[i];
                if (arg.TryRead<string>(out var str))
                {
                    await file.WriteAsync(str, cancellationToken);
                }
                else if (arg.TryRead<double>(out var d))
                {
                    using PooledArray<char> fileBuffer = new(64);
                    var span = fileBuffer.AsSpan();
                    NumberFormatter.TryFormat(
                        d,
                        span,
                        out var charsWritten,
                        context.State.Platform.NumberFormat
                    );
                    await file.WriteAsync(
                        fileBuffer.UnderlyingArray.AsMemory(0, charsWritten),
                        cancellationToken
                    );
                }
                else
                {
                    LuaRuntimeException.BadArgument(context.State, i + 1, name);
                }
            }
        }
        catch (IOException ex)
        {
            context.State.Stack.PopUntil(context.ReturnFrameBase);
            var stack = context.State.Stack;
            stack.Push(LuaValue.Nil);
            stack.Push(context.State.Platform.EncodeText(ex.Message));
            stack.Push(ex.HResult);
            return 3;
        }

        context.State.Stack.PopUntil(context.ReturnFrameBase);
        context.State.Stack.Push(new(file));
        return 1;
    }

    static readonly LuaValue[] defaultReadFormat = ["*l"];

    public static async ValueTask<int> ReadAsync(
        LuaState state,
        FileHandle file,
        string name,
        int startArgumentIndex,
        ReadOnlyMemory<LuaValue> formats,
        bool throwError,
        CancellationToken cancellationToken
    )
    {
        if (!file.IsOpen)
        {
            throw new LuaRuntimeException(state, "attempt to use a closed file");
        }

        if (formats.Length == 0)
        {
            formats = defaultReadFormat;
        }

        var stack = state.Stack;
        var top = stack.Count;

        try
        {
            for (var i = 0; i < formats.Length; i++)
            {
                var format = formats.Span[i];
                if (format.TryRead<string>(out var str))
                {
                    switch (str)
                    {
                        case "*n":
                        case "*number":
                            var number = await file.ReadNumberAsync(cancellationToken);
                            stack.Push(number ?? LuaValue.Nil);
                            if (number == null)
                            {
                                goto ReadFailed;
                            }
                            break;
                        case "*a":
                        case "*all":
                            stack.Push(await file.ReadToEndAsync(cancellationToken));
                            break;
                        case "*l":
                        case "*line":
                            var line = await file.ReadLineAsync(false, cancellationToken);
                            stack.Push(line ?? LuaValue.Nil);
                            if (line == null)
                            {
                                goto ReadFailed;
                            }
                            break;
                        case "L":
                        case "*L":
                            var text = await file.ReadLineAsync(true, cancellationToken);
                            stack.Push(text == null ? LuaValue.Nil : text);
                            if (text == null)
                            {
                                goto ReadFailed;
                            }
                            break;
                        default:
                            LuaRuntimeException.BadArgument(
                                state,
                                startArgumentIndex + i + 1,
                                "invalid format"
                            );
                            break;
                    }
                }
                else if (format.Type == LuaValueType.Number)
                {
                    var length = Math.Truncate(format.UnsafeReadDouble());
                    if (double.IsNaN(length) || length < 0 || length > int.MaxValue)
                        LuaRuntimeException.BadArgument(
                            state,
                            startArgumentIndex + i + 1,
                            "invalid count"
                        );
                    var count = (int)length;
                    var ret = await file.ReadStringAsync(count, cancellationToken);
                    if (ret == null)
                    {
                        stack.Push(default);
                        goto ReadFailed;
                    }
                    else
                    {
                        stack.Push(ret);
                    }
                }
                else
                {
                    LuaRuntimeException.BadArgument(
                        state,
                        startArgumentIndex + i + 1,
                        ["string", "integer"],
                        format.TypeToString()
                    );
                }
            }

            return formats.Length;

            ReadFailed:
            if (throwError && stack.Get(top).Type == LuaValueType.Nil)
            {
                stack.PopUntil(top);
                return 0;
            }
            return stack.Count - top;
        }
        catch (IOException ex)
        {
            if (throwError)
            {
                throw;
            }

            stack.PopUntil(top);
            stack.Push(LuaValue.Nil);
            stack.Push(state.Platform.EncodeText(ex.Message));
            stack.Push(ex.HResult);
            return 3;
        }
    }
}
