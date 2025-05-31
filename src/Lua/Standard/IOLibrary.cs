using Lua.IO;
using Lua.Runtime;
using Lua.Standard.Internal;

namespace Lua.Standard;

public sealed class IOLibrary
{
    public static readonly IOLibrary Instance = new();

    public IOLibrary()
    {
        var libraryName = "io";
        Functions =
        [
            new(libraryName,"close", Close),
            new(libraryName,"flush", Flush),
            new(libraryName,"input", Input),
            new(libraryName,"lines", Lines),
            new(libraryName,"open", Open),
            new(libraryName,"output", Output),
            new(libraryName,"read", Read),
            new(libraryName,"type", Type),
            new(libraryName,"write", Write),
            new(libraryName,"tmpfile", TmpFile),
        ];
    }

    public readonly LibraryFunction[] Functions;

    public ValueTask<int> Close(LuaFunctionExecutionContext context, CancellationToken cancellationToken)
    {
        var file = context.HasArgument(0)
            ? context.GetArgument<FileHandle>(0)
            : context.State.Registry["_IO_output"].Read<FileHandle>();

        try
        {
            file.Close();
            return new(context.Return(true));
        }
        catch (IOException ex)
        {
            return new(context.Return(LuaValue.Nil, ex.Message, ex.HResult));
        }
    }

    public async ValueTask<int> Flush(LuaFunctionExecutionContext context, CancellationToken cancellationToken)
    {
        var file = context.State.Registry["_IO_output"].Read<FileHandle>();

        try
        {
            await file.FlushAsync(cancellationToken);
            return context.Return(true);
        }
        catch (IOException ex)
        {
            return context.Return(LuaValue.Nil, ex.Message, ex.HResult);
        }
    }

    public ValueTask<int> Input(LuaFunctionExecutionContext context, CancellationToken cancellationToken)
    {
        var registry = context.State.Registry;

        if (context.ArgumentCount == 0 || context.Arguments[0].Type is LuaValueType.Nil)
        {
            return new(context.Return(registry["_IO_input"]));
        }

        var arg = context.Arguments[0];
        if (arg.TryRead<FileHandle>(out var file))
        {
            registry["_IO_input"] = new(file);
            return new(context.Return(new LuaValue(file)));
        }
        else
        {
            var stream = context.State.FileSystem.Open(arg.ToString(), LuaFileOpenMode.ReadWriteOpen);
            var handle = new FileHandle(stream);
            registry["_IO_input"] = new(handle);
            return new(context.Return(new LuaValue(handle)));
        }
    }

    public ValueTask<int> Lines(LuaFunctionExecutionContext context, CancellationToken cancellationToken)
    {
        if (context.ArgumentCount == 0)
        {
            var file = context.State.Registry["_IO_input"].Read<FileHandle>();
            return new(context.Return(new CSharpClosure("iterator", [new(file)], static async (context, cancellationToken) =>
            {
                var file = context.GetCsClosure()!.UpValues[0].Read<FileHandle>();
                context.Return();
                var resultCount = await IOHelper.ReadAsync(context.Thread, file, "io.lines", 0, Memory<LuaValue>.Empty, true, cancellationToken);
                if (resultCount > 0 && context.Thread.Stack.Get(context.ReturnFrameBase).Type is LuaValueType.Nil)
                {
                    file.Close();
                }

                return resultCount;
            })));
        }
        else
        {
            var fileName = context.GetArgument<string>(0);
            var stack = context.Thread.Stack;
            context.Return();

            IOHelper.Open(context.Thread, fileName, "r", true);

            var file = stack.Get(context.ReturnFrameBase).Read<FileHandle>();
            var upValues = new LuaValue[context.Arguments.Length];
            upValues[0] = new(file);
            context.Arguments[1..].CopyTo(upValues[1..]);

            return new(context.Return(new CSharpClosure("iterator", upValues, static async (context, cancellationToken) =>
            {
                var upValues = context.GetCsClosure()!.UpValues;
                var file = upValues[0].Read<FileHandle>();
                var formats = upValues.AsMemory(1);
                var stack = context.Thread.Stack;
                context.Return();
                var resultCount = await IOHelper.ReadAsync(context.Thread, file, "io.lines", 0, formats, true, cancellationToken);
                if (resultCount > 0 && stack.Get(context.ReturnFrameBase).Type is LuaValueType.Nil)
                {
                    file.Close();
                }

                return resultCount;
            })));
        }
    }

    public ValueTask<int> Open(LuaFunctionExecutionContext context, CancellationToken cancellationToken)
    {
        var fileName = context.GetArgument<string>(0);
        var mode = context.HasArgument(1)
            ? context.GetArgument<string>(1)
            : "r";
        context.Return();
        try
        {
            var resultCount = IOHelper.Open(context.Thread, fileName, mode, true);
            return new(resultCount);
        }
        catch (IOException ex)
        {
            return new(context.Return(LuaValue.Nil, ex.Message, ex.HResult));
        }
    }

    public ValueTask<int> Output(LuaFunctionExecutionContext context, CancellationToken cancellationToken)
    {
        var io = context.State.Registry;

        if (context.ArgumentCount == 0 || context.Arguments[0].Type is LuaValueType.Nil)
        {
            return new(context.Return(io["_IO_output"]));
        }

        var arg = context.Arguments[0];
        if (arg.TryRead<FileHandle>(out var file))
        {
            io["_IO_output"] = new(file);
            return new(context.Return(new LuaValue(file)));
        }
        else
        {
            var stream = context.State.FileSystem.Open(arg.ToString(), LuaFileOpenMode.ReadWriteOpen);
            var handle = new FileHandle(stream);
            io["_IO_output"] = new(handle);
            return new(context.Return(new LuaValue(handle)));
        }
    }

    public async ValueTask<int> Read(LuaFunctionExecutionContext context, CancellationToken cancellationToken)
    {
        var file = context.State.Registry["_IO_input"].Read<FileHandle>();
        var args = context.Arguments.ToArray();
        context.Return();

        var resultCount = await IOHelper.ReadAsync(context.Thread, file, "io.read", 0, args, false, cancellationToken);
        return resultCount;
    }

    public ValueTask<int> Type(LuaFunctionExecutionContext context, CancellationToken cancellationToken)
    {
        var arg0 = context.GetArgument(0);

        if (arg0.TryRead<FileHandle>(out var file))
        {
            return new(context.Return(file.IsClosed ? "closed file" : "file"));
        }
        else
        {
            return new(context.Return(LuaValue.Nil));
        }
    }

    public async ValueTask<int> Write(LuaFunctionExecutionContext context, CancellationToken cancellationToken)
    {
        var file = context.State.Registry["_IO_output"].Read<FileHandle>();
        var resultCount = await IOHelper.WriteAsync(file, "io.write", context, cancellationToken);
        return resultCount;
    }

    public ValueTask<int> TmpFile(LuaFunctionExecutionContext context, CancellationToken cancellationToken)
    {
        return new(context.Return(LuaValue.FromUserData(new FileHandle(context.State.FileSystem.OpenTempFileStream()))));
    }
}