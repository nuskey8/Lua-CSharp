using System.Text;
using Lua.Internal;
using Lua.IO;

namespace Lua.Standard.Internal;

internal static class IOHelper
{
    public static int Open(LuaThread thread, string fileName, string mode, bool throwError)
    {
        var fileMode = mode switch
        {
            "r" or "rb" => LuaFileOpenMode.Read,
            "w" or "wb" => LuaFileOpenMode.Write,
            "a" or "ab" => LuaFileOpenMode.Append,
            "r+" or "rb+" => LuaFileOpenMode.ReadWriteOpen,
            "w+" or "wb+" => LuaFileOpenMode.ReadWriteCreate,
            "a+" or "ab+" => LuaFileOpenMode.ReadAppend,
            _ => throw new LuaRuntimeException(thread, "bad argument #2 to 'open' (invalid mode)"),
        };

        var binary = mode.Contains("b");
        if (binary) throw new LuaRuntimeException(thread, "binary mode is not supported");

        try
        {
            var stream = thread.State.FileSystem.Open(fileName, fileMode, throwError);
            if (stream == null)
            {
                return 0;
            }

            thread.Stack.Push(new LuaValue(new FileHandle(stream)));
            return 1;
        }
        catch (IOException ex)
        {
            if (throwError)
            {
                throw;
            }

            thread.Stack.Push(LuaValue.Nil);
            thread.Stack.Push(ex.Message);
            thread.Stack.Push(ex.HResult);
            return 3;
        }
    }

    // TODO: optimize (use IBuffertWrite<byte>, async)

    public static async ValueTask<int> WriteAsync(FileHandle file, string name, LuaFunctionExecutionContext context, CancellationToken cancellationToken)
    {
        try
        {
            for (int i = 0; i < context.ArgumentCount; i++)
            {
                var arg = context.Arguments[i];
                if (arg.TryRead<string>(out var str))
                {
                    await file.WriteAsync(str.AsMemory(), cancellationToken);
                }
                else if (arg.TryRead<double>(out var d))
                {
                    using var fileBuffer = new PooledArray<char>(64);
                    var span = fileBuffer.AsSpan();
                    d.TryFormat(span, out var charsWritten);
                    await file.WriteAsync(fileBuffer.AsMemory()[..charsWritten], cancellationToken);
                }
                else
                {
                    LuaRuntimeException.BadArgument(context.Thread, i + 1, name);
                }
            }
        }
        catch (IOException ex)
        {
            context.Thread.Stack.PopUntil(context.ReturnFrameBase);
            var stack = context.Thread.Stack;
            stack.Push(LuaValue.Nil);
            stack.Push(ex.Message);
            stack.Push(ex.HResult);
            return 3;
        }

        context.Thread.Stack.PopUntil(context.ReturnFrameBase);
        context.Thread.Stack.Push(new(file));
        return 1;
    }

    static readonly LuaValue[] defaultReadFormat = ["*l"];

    public static async ValueTask<int> ReadAsync(LuaThread thread, FileHandle file, string name, int startArgumentIndex, ReadOnlyMemory<LuaValue> formats, bool throwError, CancellationToken cancellationToken)
    {
        if (formats.Length == 0)
        {
            formats = defaultReadFormat;
        }

        var stack = thread.Stack;
        var top = stack.Count;

        try
        {
            for (int i = 0; i < formats.Length; i++)
            {
                var format = formats.Span[i];
                if (format.TryRead<string>(out var str))
                {
                    switch (str)
                    {
                        case "*n":
                        case "*number":
                            // TODO: support number format
                            throw new NotImplementedException();
                        case "*a":
                        case "*all":
                            stack.Push(await file.ReadToEndAsync(cancellationToken));
                            break;
                        case "*l":
                        case "*line":
                            stack.Push(await file.ReadLineAsync(cancellationToken) ?? LuaValue.Nil);
                            break;
                        case "L":
                        case "*L":
                            var text = await file.ReadLineAsync(cancellationToken);
                            stack.Push(text == null ? LuaValue.Nil : text + Environment.NewLine);
                            break;
                    }
                }
                else if (format.TryRead<int>(out var count))
                {
                    var ret = await file.ReadStringAsync(count, cancellationToken);
                    if (ret == null)
                    {
                        stack.PopUntil(top);
                        stack.Push(default);
                        return 1;
                    }
                    else
                    {
                        stack.Push(ret);
                    }
                }
                else
                {
                    LuaRuntimeException.BadArgument(thread, i + 1, name);
                }
            }

            return formats.Length;
        }
        catch (IOException ex)
        {
            if (throwError)
            {
                throw;
            }

            stack.PopUntil(top);
            stack.Push(LuaValue.Nil);
            stack.Push(ex.Message);
            stack.Push(ex.HResult);
            return 3;
        }
    }
}