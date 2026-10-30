# Operating system

[Reference index](README.md) · [Official Lua 5.2 OS library](https://www.lua.org/manual/5.2/manual.html#6.9) · [C Lua 5.2.4 source](https://www.lua.org/source/5.2/loslib.c.html) · [Implementation](../../src/Lua/Standard/OperatingSystemLibrary.cs)

Install with `state.OpenOperatingSystemLibrary()`. Time, environment, process exit, and filesystem operations are routed through `LuaPlatform` providers where indicated. Default providers use .NET APIs; Unity/custom providers can behave differently.

| API / official reference | Inputs and result | Lua-CSharp implementation notes and differences |
| --- | --- | --- |
| [`os.clock()`](https://www.lua.org/manual/5.2/manual.html#pdf-os.clock) | Returns elapsed CPU time in seconds. | **Host-dependent:** calls `ILuaOsEnvironment.GetTotalProcessorTime`. The default reads the current .NET process's total processor time, including host work outside Lua. |
| [`os.date([format [, time]])`](https://www.lua.org/manual/5.2/manual.html#pdf-os.date) | Formats a timestamp, or returns date fields for `*t`; a leading `!` selects UTC. Defaults to current time and `%c`. | Uses `DateTime`, `TimeProvider`, and invariant .NET formatting rather than C `strftime`. **Limitation:** several conversions are incomplete or differ; see below. |
| [`os.difftime(t2, t1)`](https://www.lua.org/manual/5.2/manual.html#pdf-os.difftime) | Returns `t2 - t1` in seconds. | Direct double subtraction preserves fractions instead of converting to C `time_t` first. **Limitation:** both arguments are required; C Lua 5.2.4 also accepts an omitted/nil second argument as zero, although the manual lists both arguments. |
| [`os.execute([command])`](https://www.lua.org/manual/5.2/manual.html#pdf-os.execute) | Officially runs a shell command or queries shell availability. | **Unsupported operation:** no command/nil returns false; supplying a command raises a not-supported error. The library has no shell-execution implementation. |
| [`os.exit([code [, close]])`](https://www.lua.org/manual/5.2/manual.html#pdf-os.exit) | Requests termination with a numeric code or boolean success/failure code. | **Host-dependent:** default `SystemOsEnvironment` calls `Environment.Exit`; Unity uses its application exit provider. **Limitations:** `close` is ignored, so the Lua state is not explicitly disposed before exiting. The numeric branch requires an integral 32-bit Lua number through a strict managed `int` read; fractional numbers and numeric strings are rejected instead of converted by C Lua's integer argument reader. |
| [`os.getenv(name)`](https://www.lua.org/manual/5.2/manual.html#pdf-os.getenv) | Returns an environment value or nil. | **Host-dependent:** default provider reads process environment variables; custom/Unity providers may expose a managed dictionary instead. Names/results are converted between byte strings and .NET text when needed. |
| [`os.remove(filename)`](https://www.lua.org/manual/5.2/manual.html#pdf-os.remove) | Removes a path or reports failure. | **Limitation:** default `FileSystem.Remove` removes files only, even on POSIX hosts where C Lua can remove empty directories. It uses `File.Exists`/`File.Delete` and has no directory-removal path. Handled errors use .NET HResult codes. |
| [`os.rename(oldname, newname)`](https://www.lua.org/manual/5.2/manual.html#pdf-os.rename) | Moves/renames a file or directory; reports success or handled failure. | **Host-dependent:** default provider uses `File.Move`/`File.Replace` and `Directory.Move`. Directory replacement restrictions follow .NET/host filesystem behavior rather than C `rename`; .NET Standard 2.1 uses `File.Replace` for an existing file destination. |
| [`os.setlocale(locale [, category])`](https://www.lua.org/manual/5.2/manual.html#pdf-os.setlocale) | Officially selects or queries a C locale. | **Limitation:** only the literal `C` is accepted; other strings return nil, nil is not a locale-query operation, and `category` is ignored. No C or .NET locale is changed. Date formatting stays invariant; string casing/classification follows the [string library's rules](string.md). |
| [`os.time([table])`](https://www.lua.org/manual/5.2/manual.html#pdf-os.time) | Without a table, returns current Unix seconds. With a table, converts local date fields; defaults are hour 12, minute/second 0. The input table remains unchanged, as in C Lua 5.2.4. | **Limitation:** fields must be integral and fit the .NET `DateTime` constructor; fractional fields are rejected instead of truncated, and out-of-range dates are rejected instead of normalized by C `mktime`. `isdst` is ignored. Current time comes from `TimeProvider`; table conversion uses the system local timezone. |
| [`os.tmpname()`](https://www.lua.org/manual/5.2/manual.html#pdf-os.tmpname) | Returns a temporary pathname. | **Host-dependent:** default `Path.GetTempFileName` creates an empty file while choosing the name. The caller owns cleanup; unlike `io.tmpfile`, this API does not attach delete-on-close behavior. The name uses the state's string representation. |

## Date conversion details

The date formatter is a managed mapping, not a complete C `strftime` implementation. The following conversions are recognized:

`%a %A %b %B %c %C %d %D %e %F %g %G %h %H %I %j %m %M %n %p %r %R %S %t %T %u %U %V %w %W %x %X %y %Y %z %Z %%`

Known formatting differences and incomplete conversions include:

- `%U`, `%V`, and `%W` currently emit the literal `??`; week-number calculation is not implemented.
- `%g`/`%G` use the calendar year, not the ISO week-based year defined for those conversions by POSIX `strftime`. Some C Lua builds reject these conversions entirely.
- `%z` and `%Z` both use .NET's `zzz` offset format, including a colon, rather than separate C numeric-offset/timezone-name conversions. With omitted time, the formatter receives a `DateTime` without timezone-kind information, so even a leading `!` can produce the system local offset in these two conversions.
- `%c`, `%x`, `%X`, month/day names, and related patterns use invariant .NET formatting; changing `os.setlocale` has no effect. In particular, `%c` maps to .NET's long-date/short-time pattern and omits seconds; `%x` uses its short-date pattern; `%r` uses an unpadded 12-hour clock hour.
- `%E`/`%O` modifiers and unknown conversions raise an error.

Timestamp conversion is bounded by .NET date/time ranges. For byte-string states, format literals are represented as byte-string units; the formatter's generated names come from invariant .NET date strings. Use the official link for the complete Lua/C contract and this list for the implemented behavior.
