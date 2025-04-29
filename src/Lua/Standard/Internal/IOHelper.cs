using System.Text;
using Lua.Internal;

namespace Lua.Standard.Internal;

internal static class IOHelper
{
    public static int Open(LuaThread thread, string fileName, string mode, bool throwError)
    {
        var fileMode = mode switch
        {
            "r" or "rb" or "r+" or "r+b" => FileMode.Open,
            "w" or "wb" or "w+" or "w+b" => FileMode.Create,
            "a" or "ab" or "a+" or "a+b" => FileMode.Append,
            _ => throw new LuaRuntimeException(thread.GetTraceback(), "bad argument #2 to 'open' (invalid mode)"),
        };

        var fileAccess = mode switch
        {
            "r" or "rb" => FileAccess.Read,
            "w" or "wb" or "a" or "ab" => FileAccess.Write,
            _ => FileAccess.ReadWrite,
        };

        try
        {
            var stream = File.Open(fileName, fileMode, fileAccess);
            thread.Push(new LuaValue(new FileHandle(stream)));
            return 1;
        }
        catch (IOException ex)
        {
            if (throwError)
            {
                throw;
            }

            thread.Push(LuaValue.Nil);
            thread.Push(ex.Message);
            thread.Push(ex.HResult);
            return 3;
        }
    }

    // TODO: optimize (use IBuffertWrite<byte>, async)

    public static int Write(FileHandle file, string name, LuaFunctionExecutionContext context)
    {
        try
        {
            for (int i = 1; i < context.ArgumentCount; i++)
            {
                var arg = context.Arguments[i];
                if (arg.TryRead<string>(out var str))
                {
                    file.Write(str);
                }
                else if (arg.TryRead<double>(out var d))
                {
                    using var fileBuffer = new PooledArray<char>(64);
                    var span = fileBuffer.AsSpan();
                    d.TryFormat(span, out var charsWritten);
                    file.Write(span[..charsWritten]);
                }
                else
                {
                    LuaRuntimeException.BadArgument(context.Thread.GetTraceback(), i + 1, name);
                }
            }
        }
        catch (IOException ex)
        {
            var stack = context.Thread.Stack;
            stack.Push(LuaValue.Nil);
            stack.Push(ex.Message);
            stack.Push(ex.HResult);
            return 3;
        }

        context.Thread.Stack.Push(new(file));
        return 1;
    }

    static readonly LuaValue[] defaultReadFormat = ["*l"];

    public static int Read(LuaThread thread, FileHandle file, string name, int startArgumentIndex, ReadOnlySpan<LuaValue> formats, bool throwError)
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
                var format = formats[i];
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
                            stack.Push(file.ReadToEnd());
                            break;
                        case "*l":
                        case "*line":
                            stack.Push(file.ReadLine() ?? LuaValue.Nil);
                            break;
                        case "L":
                        case "*L":
                            var text = file.ReadLine();
                            stack.Push(text == null ? LuaValue.Nil : text + Environment.NewLine);
                            break;
                    }
                }
                else if (format.TryRead<int>(out var count))
                {
                    using var byteBuffer = new PooledArray<byte>(count);

                    for (int j = 0; j < count; j++)
                    {
                        var b = file.ReadByte();
                        if (b == -1)
                        {
                            stack.PopUntil(top);
                            stack.Push(LuaValue.Nil);
                            return 1;
                        }

                        byteBuffer[j] = (byte)b;
                    }

                    stack.Push(Encoding.UTF8.GetString(byteBuffer.AsSpan()));
                }
                else
                {
                    LuaRuntimeException.BadArgument(thread.GetTraceback(), i + 1, name);
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