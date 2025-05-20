using Lua.IO;
using Lua.Runtime;
using Lua.Standard.Internal;

namespace Lua.Standard;

// TODO: optimize (remove StreamReader/Writer)

public class FileHandle : ILuaUserData
{
    public static readonly LuaFunction IndexMetamethod = new("index", (context, ct) =>
    {
        context.GetArgument<FileHandle>(0);
        var key = context.GetArgument(1);

        if (key.TryRead<string>(out var name))
        {
            return new(context.Return(name switch
            {
                "close" => CloseFunction!,
                "flush" => FlushFunction!,
                "lines" => LinesFunction!,
                "read" => ReadFunction!,
                "seek" => SeekFunction!,
                "setvbuf" => SetVBufFunction!,
                "write" => WriteFunction!,
                _ => LuaValue.Nil,
            }));
        }
        else
        {
            return new(context.Return(LuaValue.Nil));
        }
    });

    IStream stream;
    bool isClosed;

    public bool IsClosed => Volatile.Read(ref isClosed);

    LuaTable? ILuaUserData.Metatable { get => fileHandleMetatable; set => fileHandleMetatable = value; }

    static LuaTable? fileHandleMetatable;

    static FileHandle()
    {
        fileHandleMetatable = new LuaTable(0, 1);
        fileHandleMetatable[Metamethods.Index] = IndexMetamethod;
    }

    public FileHandle(Stream stream) : this(new StreamWrapper(stream)) { }

    public FileHandle(IStream stream)
    {
        this.stream = stream;
    }

    public ValueTask<string?> ReadLineAsync(CancellationToken cancellationToken)
    {
        return stream.ReadLineAsync(cancellationToken);
    }

    public ValueTask<string> ReadToEndAsync(CancellationToken cancellationToken)
    {
        return stream.ReadToEndAsync(cancellationToken);
    }

    public ValueTask<string?> ReadStringAsync(int count, CancellationToken cancellationToken)
    {
        return stream.ReadStringAsync(count, cancellationToken);
    }

    public ValueTask WriteAsync(ReadOnlyMemory<char> buffer, CancellationToken cancellationToken)
    {
        return stream.WriteAsync(buffer, cancellationToken);
    }

    public long Seek(string whence, long offset) =>
        whence switch
        {
            "set" => stream.Seek(offset, SeekOrigin.Begin),
            "cur" => stream.Seek(offset, SeekOrigin.Current),
            "end" => stream.Seek(offset, SeekOrigin.End),
            _ => throw new ArgumentException($"Invalid option '{whence}'")
        };

    public ValueTask FlushAsync(CancellationToken cancellationToken)
    {
        return stream.FlushAsync(cancellationToken);
    }

    public void SetVBuf(string mode, int size)
    {
        stream.SetVBuf(mode, size);
    }

    public void Close()
    {
        if (isClosed) throw new ObjectDisposedException(nameof(FileHandle));
        Volatile.Write(ref isClosed, true);
        stream.Dispose();
        stream = null!;
    }

    static readonly LuaFunction CloseFunction = new("close", (context, cancellationToken) =>
    {
        var file = context.GetArgument<FileHandle>(0);

        try
        {
            file.Close();
            return new(context.Return(true));
        }
        catch (IOException ex)
        {
            return new(context.Return(LuaValue.Nil, ex.Message, ex.HResult));
        }
    });

    static readonly LuaFunction FlushFunction = new("flush", async (context, cancellationToken) =>
    {
        var file = context.GetArgument<FileHandle>(0);

        try
        {
            await file.FlushAsync(cancellationToken);
            return context.Return(true);
        }
        catch (IOException ex)
        {
            return (context.Return(LuaValue.Nil, ex.Message, ex.HResult));
        }
    });

    static readonly LuaFunction LinesFunction = new("lines", (context, cancellationToken) =>
    {
        var file = context.GetArgument<FileHandle>(0);
        var format = context.HasArgument(1)
            ? context.Arguments[1]
            : "*l";


        return new(context.Return(new CSharpClosure("iterator", [new(file), format], static async (context, cancellationToken) =>
        {
            var upValues = context.GetCsClosure()!.UpValues.AsMemory();
            var file = upValues.Span[0].Read<FileHandle>();
            context.Return();
            var resultCount = await IOHelper.ReadAsync(context.Thread, file, "lines", 0, upValues[1..], true, cancellationToken);
            return resultCount;
        })));
    });

    static readonly LuaFunction ReadFunction = new("read", async (context, cancellationToken) =>
    {
        var file = context.GetArgument<FileHandle>(0);
        var args = context.Arguments[1..].ToArray();
        context.Return();
        var resultCount = await IOHelper.ReadAsync(context.Thread, file, "read", 1, args, false, cancellationToken);
        return resultCount;
    });

    static readonly LuaFunction SeekFunction = new("seek", (context, cancellationToken) =>
    {
        var file = context.GetArgument<FileHandle>(0);
        var whence = context.HasArgument(1)
            ? context.GetArgument<string>(1)
            : "cur";
        var offset = context.HasArgument(2)
            ? context.GetArgument<int>(2)
            : 0;

        if (whence is not ("set" or "cur" or "end"))
        {
            throw new LuaRuntimeException(context.Thread, $"bad argument #2 to 'seek' (invalid option '{whence}')");
        }

        try
        {
            return new(context.Return(file.Seek(whence, (long)offset)));
        }
        catch (IOException ex)
        {
            return new(context.Return(LuaValue.Nil, ex.Message, ex.HResult));
        }
    });

    static readonly LuaFunction SetVBufFunction = new("setvbuf", (context, cancellationToken) =>
    {
        var file = context.GetArgument<FileHandle>(0);
        var mode = context.GetArgument<string>(1);
        var size = context.HasArgument(2)
            ? context.GetArgument<int>(2)
            : -1;

        file.SetVBuf(mode, size);

        return new(context.Return(true));
    });

    static readonly LuaFunction WriteFunction = new("write", async (context, cancellationToken) =>
    {
        var file = context.GetArgument<FileHandle>(0);
        var resultCount = await IOHelper.WriteAsync(file, "write", context with { ArgumentCount = context.ArgumentCount - 1 }, cancellationToken);
        return resultCount;
    });
}