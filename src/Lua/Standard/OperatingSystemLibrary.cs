using Lua.Standard.Internal;

namespace Lua.Standard;

public sealed class OperatingSystemLibrary
{
    public static readonly OperatingSystemLibrary Instance = new();

    public OperatingSystemLibrary()
    {
        Functions =
        [
            new("os", "clock", Clock),
            new("os", "date", Date),
            new("os", "difftime", DiffTime),
            new("os", "execute", Execute),
            new("os", "exit", Exit),
            new("os", "getenv", GetEnv),
            new("os", "remove", Remove),
            new("os", "rename", Rename),
            new("os", "setlocale", SetLocale),
            new("os", "time", Time),
            new("os", "tmpname", TmpName)
        ];
    }

    public readonly LibraryFunction[] Functions;

    public ValueTask<int> Clock(LuaFunctionExecutionContext context, CancellationToken cancellationToken)
    {
        return new(context.Return(context.GlobalState.OsEnvironment.GetTotalProcessorTime()));
    }

    public ValueTask<int> Date(LuaFunctionExecutionContext context, CancellationToken cancellationToken)
    {
        var format = context.HasArgument(0)
            ? context.GetArgument<string>(0).AsSpan()
            : "%c".AsSpan();

        DateTime now;
        if (context.HasArgument(1))
        {
            var time = context.GetArgument<double>(1);
            now = DateTimeHelper.FromUnixTime(time);
        }
        else
        {
            now = context.GlobalState.TimeProvider.GetUtcNow().DateTime;
        }

        var isDst = false;
        if (format[0] == '!')
        {
            format = format[1..];
        }
        else
        {
            now = context.GlobalState.TimeProvider.GetLocalNow().DateTime;
            isDst = now.IsDaylightSavingTime();
        }

        if (format is "*t")
        {
            LuaTable table = new()
            {
                ["year"] = now.Year,
                ["month"] = now.Month,
                ["day"] = now.Day,
                ["hour"] = now.Hour,
                ["min"] = now.Minute,
                ["sec"] = now.Second,
                ["wday"] = (int)now.DayOfWeek + 1,
                ["yday"] = now.DayOfYear,
                ["isdst"] = isDst
            };

            return new(context.Return(table));
        }
        else
        {
            return new(context.Return(DateTimeHelper.StrFTime(context.State, format, now)));
        }
    }

    public ValueTask<int> DiffTime(LuaFunctionExecutionContext context, CancellationToken cancellationToken)
    {
        var t2 = context.GetArgument<double>(0);
        var t1 = context.GetArgument<double>(1);
        return new(context.Return(t2 - t1));
    }

    public ValueTask<int> Execute(LuaFunctionExecutionContext context, CancellationToken cancellationToken)
    {
        // os.execute(command) is not supported

        if (context.HasArgument(0))
        {
            throw new NotSupportedException("os.execute(command) is not supported");
        }
        else
        {
            return new(context.Return(false));
        }
    }

    public async ValueTask<int> Exit(LuaFunctionExecutionContext context, CancellationToken cancellationToken)
    {
        // Ignore 'close' parameter
        var exitCode = 0;
        if (context.HasArgument(0))
        {
            var code = context.Arguments[0];

            if (code.TryRead<bool>(out var b))
            {
                exitCode = b ? 0 : 1;
            }
            else if (code.TryRead<int>(out var d))
            {
                exitCode = d;
            }
            else
            {
                LuaRuntimeException.BadArgument(context.State, 1, LuaValueType.Nil, code.Type);
            }
        }

        await context.GlobalState.OsEnvironment.Exit(exitCode, cancellationToken);
        throw new InvalidOperationException("Unreachable code.. reached.");
    }

    public ValueTask<int> GetEnv(LuaFunctionExecutionContext context, CancellationToken cancellationToken)
    {
        var variable = context.GetArgument<string>(0);
        return new(context.Return(context.GlobalState.OsEnvironment.GetEnvironmentVariable(variable) ?? LuaValue.Nil));
    }

    public async ValueTask<int> Remove(LuaFunctionExecutionContext context, CancellationToken cancellationToken)
    {
        var fileName = context.GetArgument<string>(0);
        try
        {
            await context.GlobalState.FileSystem.Remove(fileName, cancellationToken);
            return context.Return(true);
        }
        catch (IOException ex)
        {
            return context.Return(LuaValue.Nil, ex.Message, ex.HResult);
        }
    }

    public async ValueTask<int> Rename(LuaFunctionExecutionContext context, CancellationToken cancellationToken)
    {
        var oldName = context.GetArgument<string>(0);
        var newName = context.GetArgument<string>(1);
        try
        {
            await context.GlobalState.FileSystem.Rename(oldName, newName, cancellationToken);
            return context.Return(true);
        }
        catch (IOException ex)
        {
            return context.Return(LuaValue.Nil, ex.Message, ex.HResult);
        }
    }

    public ValueTask<int> SetLocale(LuaFunctionExecutionContext context, CancellationToken cancellationToken)
    {
        // os.setlocale is not supported (always return nil)
        return new(context.Return(LuaValue.Nil));
    }

    public ValueTask<int> Time(LuaFunctionExecutionContext context, CancellationToken cancellationToken)
    {
        if (context.HasArgument(0))
        {
            var table = context.GetArgument<LuaTable>(0);
            var date = DateTimeHelper.ParseTimeTable(context.State, table);
            return new(context.Return(DateTimeHelper.GetUnixTime(date)));
        }
        else
        {
            return new(context.Return(DateTimeHelper.GetUnixTime(context.GlobalState.TimeProvider.GetUtcNow().DateTime)));
        }
    }

    public ValueTask<int> TmpName(LuaFunctionExecutionContext context, CancellationToken cancellationToken)
    {
        return new(context.Return(context.GlobalState.FileSystem.GetTempFileName()));
    }
}