using Lua.Runtime;
using Lua.Standard.Internal;

namespace Lua.Standard;

public sealed class IOLibrary
{
    public static readonly IOLibrary Instance = new();

    public IOLibrary()
    {
        Functions =
        [
            new("close", Close),
            new("flush", Flush),
            new("input", Input),
            new("lines", Lines),
            new("open", Open),
            new("output", Output),
            new("read", Read),
            new("type", Type),
            new("write", Write),
        ];
    }

    public readonly LuaFunction[] Functions;

    public ValueTask<int> Close(LuaFunctionExecutionContext context, CancellationToken cancellationToken)
    {
        var file = context.HasArgument(0)
            ? context.GetArgument<FileHandle>(0)
            : context.State.Environment["io"].Read<LuaTable>()["stdout"].Read<FileHandle>();

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

    public ValueTask<int> Flush(LuaFunctionExecutionContext context, CancellationToken cancellationToken)
    {
        var file = context.State.Environment["io"].Read<LuaTable>()["stdout"].Read<FileHandle>();

        try
        {
            file.Flush();
            return new(context.Return(true));
        }
        catch (IOException ex)
        {
            return new(context.Return(LuaValue.Nil, ex.Message, ex.HResult));
        }
    }

    public ValueTask<int> Input(LuaFunctionExecutionContext context, CancellationToken cancellationToken)
    {
        var io = context.State.Environment["io"].Read<LuaTable>();

        if (context.ArgumentCount == 0 || context.Arguments[0].Type is LuaValueType.Nil)
        {
            return new(context.Return(io["stdio"]));
        }

        var arg = context.Arguments[0];
        if (arg.TryRead<FileHandle>(out var file))
        {
            io["stdio"] = new(file);
            return new(context.Return(new LuaValue(file)));
        }
        else
        {
            var stream = File.Open(arg.ToString()!, FileMode.Open, FileAccess.ReadWrite);
            var handle = new FileHandle(stream);
            io["stdio"] = new(handle);
            return new(context.Return(new LuaValue(handle)));
        }
    }

    public ValueTask<int> Lines(LuaFunctionExecutionContext context, CancellationToken cancellationToken)
    {
        if (context.ArgumentCount == 0)
        {
            var file = context.State.Environment["io"].Read<LuaTable>()["stdio"].Read<FileHandle>();
            return new(context.Return(new CSharpClosure("iterator", [new(file)], static (context, ct) =>
            {
                var file = context.GetCsClosure()!.UpValues[0].Read<FileHandle>();
                context.Return();
                var resultCount = IOHelper.Read(context.Thread, file, "lines", 0, [], true);
                if (resultCount > 0 && context.Thread.Stack.Get(context.ReturnFrameBase).Type is LuaValueType.Nil)
                {
                    file.Close();
                }

                return new(resultCount);
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

            return new(context.Return(new CSharpClosure("iterator", upValues, static (context, ct) =>
            {
                var upValues = context.GetCsClosure()!.UpValues;
                var file = upValues[0].Read<FileHandle>();
                var formats = upValues.AsSpan(1);
                var stack = context.Thread.Stack;
                context.Return();
                var resultCount = IOHelper.Read(context.Thread, file, "lines", 0, formats, true);
                if (resultCount > 0 && stack.Get(context.ReturnFrameBase).Type is LuaValueType.Nil)
                {
                    file.Close();
                }

                return new(resultCount);
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
        var resultCount = IOHelper.Open(context.Thread, fileName, mode, false);
        return new(resultCount);
    }

    public ValueTask<int> Output(LuaFunctionExecutionContext context, CancellationToken cancellationToken)
    {
        var io = context.State.Environment["io"].Read<LuaTable>();

        if (context.ArgumentCount == 0 || context.Arguments[0].Type is LuaValueType.Nil)
        {
            return new(context.Return(io["stdout"]));
        }

        var arg = context.Arguments[0];
        if (arg.TryRead<FileHandle>(out var file))
        {
            io["stdout"] = new(file);
            return new(context.Return(new LuaValue(file)));
        }
        else
        {
            var stream = File.Open(arg.ToString()!, FileMode.Open, FileAccess.ReadWrite);
            var handle = new FileHandle(stream);
            io["stdout"] = new(handle);
            return new(context.Return(new LuaValue(handle)));
        }
    }

    public ValueTask<int> Read(LuaFunctionExecutionContext context, CancellationToken cancellationToken)
    {
        var file = context.State.Environment["io"].Read<LuaTable>()["stdio"].Read<FileHandle>();
        context.Return();
        var stack = context.Thread.Stack;

        var resultCount = IOHelper.Read(context.Thread, file, "read", 0, context.Arguments, false);
        return new(resultCount);
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

    public ValueTask<int> Write(LuaFunctionExecutionContext context, CancellationToken cancellationToken)
    {
        var file = context.State.Environment["io"].Read<LuaTable>()["stdout"].Read<FileHandle>();
        context.Return();
        var resultCount = IOHelper.Write(file, "write", context);
        return new(resultCount);
    }
}