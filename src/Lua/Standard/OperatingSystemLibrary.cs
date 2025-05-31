using System.Diagnostics;
using Lua.Standard.Internal;

namespace Lua.Standard;

public sealed class OperatingSystemLibrary
{
    public static readonly OperatingSystemLibrary Instance = new();

    public OperatingSystemLibrary()
    {
        Functions =
        [
            new("os","clock", Clock),
            new("os","date", Date),
            new("os","difftime", DiffTime),
            new("os","execute", Execute),
            new("os","exit", Exit),
            new("os","getenv", GetEnv),
            new("os","remove", Remove),
            new("os","rename", Rename),
            new("os","setlocale", SetLocale),
            new("os","time", Time),
            new("os","tmpname", TmpName),
        ];
    }

    public readonly LibraryFunction[] Functions;

    public ValueTask<int> Clock(LuaFunctionExecutionContext context, CancellationToken cancellationToken)
    {
        return new(context.Return(DateTimeHelper.GetUnixTime(DateTime.UtcNow, Process.GetCurrentProcess().StartTime)));
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
            now = DateTime.UtcNow;
        }

        var isDst = false;
        if (format[0] == '!')
        {
            format = format[1..];
        }
        else
        {
            now = TimeZoneInfo.ConvertTimeFromUtc(now, TimeZoneInfo.Local);
            isDst = now.IsDaylightSavingTime();
        }

        if (format == "*t")
        {
            var table = new LuaTable();

            table["year"] = now.Year;
            table["month"] = now.Month;
            table["day"] = now.Day;
            table["hour"] = now.Hour;
            table["min"] = now.Minute;
            table["sec"] = now.Second;
            table["wday"] = ((int)now.DayOfWeek) + 1;
            table["yday"] = now.DayOfYear;
            table["isdst"] = isDst;

            return new(context.Return(table));
        }
        else
        {
            return new(context.Return(DateTimeHelper.StrFTime(context.Thread, format, now)));
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

    public ValueTask<int> Exit(LuaFunctionExecutionContext context, CancellationToken cancellationToken)
    {
        // Ignore 'close' parameter

        if (context.HasArgument(0))
        {
            var code = context.Arguments[0];

            if (code.TryRead<bool>(out var b))
            {
                Environment.Exit(b ? 0 : 1);
            }
            else if (code.TryRead<int>(out var d))
            {
                Environment.Exit(d);
            }
            else
            {
                LuaRuntimeException.BadArgument(context.Thread, 1, LuaValueType.Nil, code.Type);
            }
        }
        else
        {
            Environment.Exit(0);
        }

        return new(context.Return());
    }

    public ValueTask<int> GetEnv(LuaFunctionExecutionContext context, CancellationToken cancellationToken)
    {
        var variable = context.GetArgument<string>(0);
        return new(context.Return(Environment.GetEnvironmentVariable(variable) ?? LuaValue.Nil));
    }

    public ValueTask<int> Remove(LuaFunctionExecutionContext context, CancellationToken cancellationToken)
    {
        var fileName = context.GetArgument<string>(0);
        try
        {
            context.State.FileSystem.Remove(fileName);
            return new(context.Return(true));
        }
        catch (IOException ex)
        {
            return new(context.Return(LuaValue.Nil, ex.Message, ex.HResult));
        }
    }

    public ValueTask<int> Rename(LuaFunctionExecutionContext context, CancellationToken cancellationToken)
    {
        var oldName = context.GetArgument<string>(0);
        var newName = context.GetArgument<string>(1);
        try
        {
            context.State.FileSystem.Rename(oldName, newName);
            return new(context.Return(true));
        }
        catch (IOException ex)
        {
            return new(context.Return(LuaValue.Nil, ex.Message, ex.HResult));
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
            var date = DateTimeHelper.ParseTimeTable(context.Thread, table);
            return new(context.Return(DateTimeHelper.GetUnixTime(date)));
        }
        else
        {
            return new(context.Return(DateTimeHelper.GetUnixTime(DateTime.UtcNow)));
        }
    }

    public ValueTask<int> TmpName(LuaFunctionExecutionContext context, CancellationToken cancellationToken)
    {
        return new(context.Return(context.State.FileSystem.GetTempFileName()));
    }
}