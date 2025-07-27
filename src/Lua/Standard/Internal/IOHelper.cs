using System.Text;
using Lua.Internal;
using Lua.IO;
using Lua.Runtime;

namespace Lua.Standard.Internal;

static class IOHelper
{
    public static async ValueTask<int> Open(LuaThread thread, string fileName, string mode, bool throwError, CancellationToken cancellationToken)
    {
        var fileMode = LuaFileOpenModeExtensions.ParseModeFromString(mode);
        if (!fileMode.IsValid())
        {
            throw new LuaRuntimeException(thread, "bad argument #2 to 'open' (invalid mode)");
        }

        try
        {
            var stream = await thread.State.FileSystem.Open(fileName, fileMode, cancellationToken);

            thread.Stack.Push(new(new FileHandle(stream)));
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
                    d.TryFormat(span, out var charsWritten);
                    await file.WriteAsync(fileBuffer.UnderlyingArray.AsMemory(0, charsWritten), cancellationToken);
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
            for (var i = 0; i < formats.Length; i++)
            {
                var format = formats.Span[i];
                if (format.TryRead<string>(out var str))
                {
                    switch (str)
                    {
                        case "*n":
                        case "*number":
                            stack.Push(await file.ReadNumberAsync(cancellationToken) ?? LuaValue.Nil);
                            break;
                        case "*a":
                        case "*all":
                            stack.Push(await file.ReadToEndAsync(cancellationToken));
                            break;
                        case "*l":
                        case "*line":
                            stack.Push(await file.ReadLineAsync(false, cancellationToken) ?? LuaValue.Nil);
                            break;
                        case "L":
                        case "*L":
                            var text = await file.ReadLineAsync(true, cancellationToken);
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
                    LuaRuntimeException.BadArgument(thread, i + 1, ["string", "integer"], format.TypeToString());
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