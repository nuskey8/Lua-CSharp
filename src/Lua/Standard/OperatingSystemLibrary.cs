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
            new("os", "tmpname", TmpName),
        ];
    }

    public readonly LibraryFunction[] Functions;

    public ValueTask<int> Clock(
        LuaFunctionExecutionContext context,
        CancellationToken cancellationToken
    )
    {
        return new(
            context.Return(context.GlobalState.Platform.OsEnvironment.GetTotalProcessorTime())
        );
    }

    public ValueTask<int> Date(
        LuaFunctionExecutionContext context,
        CancellationToken cancellationToken
    )
    {
        var format = context.HasArgument(0) ? context.GetStringArgument(0).AsSpan() : "%c".AsSpan();
        if (format.IsEmpty)
            return new(context.Return(""));

        var useUtc = format[0] == '!';
        if (useUtc)
        {
            format = format[1..];
        }

        var provider = context.GlobalState.Platform.TimeProvider;
        var zone = useUtc ? TimeZoneInfo.Utc : provider.LocalTimeZone;
        var now = context.HasArgument(1)
            ? new DateTimeOffset(DateTimeHelper.FromUnixTime(context.GetArgument<double>(1)))
            : provider.GetUtcNow();
        now = TimeZoneInfo.ConvertTime(now, zone);

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
                ["isdst"] = zone.IsDaylightSavingTime(now),
            };

            return new(context.Return(table));
        }
        else
        {
            return new(context.Return(DateTimeHelper.StrFTime(context.State, format, now, zone)));
        }
    }

    public ValueTask<int> DiffTime(
        LuaFunctionExecutionContext context,
        CancellationToken cancellationToken
    )
    {
        var t2 = context.GetArgument<double>(0);
        var t1 = context.GetArgument<double>(1);
        return new(context.Return(t2 - t1));
    }

    public ValueTask<int> Execute(
        LuaFunctionExecutionContext context,
        CancellationToken cancellationToken
    )
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

    public async ValueTask<int> Exit(
        LuaFunctionExecutionContext context,
        CancellationToken cancellationToken
    )
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
            else
            {
                exitCode = context.GetIntegerArgument(0);
            }
        }

        await context.GlobalState.Platform.OsEnvironment.Exit(exitCode, cancellationToken);
        throw new InvalidOperationException("Unreachable code.. reached.");
    }

    public ValueTask<int> GetEnv(
        LuaFunctionExecutionContext context,
        CancellationToken cancellationToken
    )
    {
        var variable = context.GetStringArgument(0);
        var platform = context.State.Platform;
        var value = platform.OsEnvironment.GetEnvironmentVariable(platform.DecodeText(variable));
        return new(context.Return(value is null ? LuaValue.Nil : platform.EncodeText(value)));
    }

    public async ValueTask<int> Remove(
        LuaFunctionExecutionContext context,
        CancellationToken cancellationToken
    )
    {
        var fileName = context.GetStringArgument(0);
        try
        {
            await context.GlobalState.Platform.FileSystem.Remove(
                context.State.Platform.DecodeText(fileName),
                cancellationToken
            );
            return context.Return(true);
        }
        catch (IOException ex)
        {
            return context.Return(
                LuaValue.Nil,
                context.GlobalState.Platform.EncodeText(ex.Message),
                ex.HResult
            );
        }
    }

    public async ValueTask<int> Rename(
        LuaFunctionExecutionContext context,
        CancellationToken cancellationToken
    )
    {
        var oldName = context.GetStringArgument(0);
        var newName = context.GetStringArgument(1);
        try
        {
            await context.GlobalState.Platform.FileSystem.Rename(
                context.State.Platform.DecodeText(oldName),
                context.State.Platform.DecodeText(newName),
                cancellationToken
            );
            return context.Return(true);
        }
        catch (IOException ex)
        {
            return context.Return(
                LuaValue.Nil,
                context.GlobalState.Platform.EncodeText(ex.Message),
                ex.HResult
            );
        }
    }

    public ValueTask<int> SetLocale(
        LuaFunctionExecutionContext context,
        CancellationToken cancellationToken
    )
    {
        // os.setlocale is not supported besides the standard "C" POSIX locale
        // Any locale besides "C" will return nil
        var locale = context.GetStringArgument(0);
        if (locale == "C")
        {
            return new(context.Return(locale));
        }

        return new(context.Return(LuaValue.Nil));
    }

    public ValueTask<int> Time(
        LuaFunctionExecutionContext context,
        CancellationToken cancellationToken
    )
    {
        if (context.HasArgument(0))
        {
            var table = context.GetArgument<LuaTable>(0);
            var date = DateTimeHelper.ParseTimeTable(context.State, table);
            var zone = context.State.Platform.TimeProvider.LocalTimeZone;
            return new(
                context.Return(
                    DateTimeHelper.GetUnixTimeFromLocalTime(
                        date,
                        zone,
                        zone.SupportsDaylightSavingTime && table["isdst"].ToBoolean()
                    )
                )
            );
        }
        else
        {
            return new(
                context.Return(
                    (double)
                        context.GlobalState.Platform.TimeProvider.GetUtcNow().ToUnixTimeSeconds()
                )
            );
        }
    }

    public ValueTask<int> TmpName(
        LuaFunctionExecutionContext context,
        CancellationToken cancellationToken
    )
    {
        var platform = context.State.Platform;
        return new(context.Return(platform.EncodeText(platform.FileSystem.GetTempFileName())));
    }
}
