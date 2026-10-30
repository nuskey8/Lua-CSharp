# Operating system

[Reference index](README.md) · [Official Lua 5.2 OS library](https://www.lua.org/manual/5.2/manual.html#6.9) · [Implementation](../../src/Lua/Standard/OperatingSystemLibrary.cs)

Install with `state.OpenOperatingSystemLibrary()`. Time, environment, process exit, and filesystem operations are routed through `LuaPlatform` providers where indicated. Default providers use .NET APIs; Unity/custom providers can behave differently.

| API / official reference | Inputs and result | Lua-CSharp differences and reasons |
| --- | --- | --- |
| [`os.clock()`](https://www.lua.org/manual/5.2/manual.html#pdf-os.clock) | Returns elapsed CPU time in seconds. | **Host-dependent:** calls `ILuaOsEnvironment.GetTotalProcessorTime`. The default reads the current .NET process's total processor time, including host work outside Lua. |
| [`os.date([format [, time]])`](https://www.lua.org/manual/5.2/manual.html#pdf-os.date) | Formats a timestamp, or returns date fields for `*t`; a leading `!` selects UTC. Defaults to current time and `%c`. | Uses `DateTime`, `TimeProvider`, and invariant .NET formatting rather than C `strftime`. **Limitation:** several conversions are incomplete or differ; see below. |
| [`os.difftime(t2, t1)`](https://www.lua.org/manual/5.2/manual.html#pdf-os.difftime) | Returns `t2 - t1` in seconds. | Direct double subtraction, matching the Unix-seconds representation used here. |
| [`os.execute([command])`](https://www.lua.org/manual/5.2/manual.html#pdf-os.execute) | Officially runs a shell command or queries shell availability. | **Unsupported operation:** no command/nil returns false; supplying a command raises a not-supported error. The library has no shell-execution implementation. |
| [`os.exit([code [, close]])`](https://www.lua.org/manual/5.2/manual.html#pdf-os.exit) | Requests termination with a numeric code or boolean success/failure code. | **Host-dependent:** default `SystemOsEnvironment` calls `Environment.Exit`; Unity uses its application exit provider. **Limitation:** the `close` argument is ignored, so this API does not explicitly dispose the Lua state before exiting. |
| [`os.getenv(name)`](https://www.lua.org/manual/5.2/manual.html#pdf-os.getenv) | Returns an environment value or nil. | **Host-dependent:** default provider reads process environment variables; custom/Unity providers may expose a managed dictionary instead. Names/results are converted between byte strings and .NET text when needed. |
| [`os.remove(filename)`](https://www.lua.org/manual/5.2/manual.html#pdf-os.remove) | Removes a path or reports failure. | **Limitation:** default `FileSystem.Remove` removes files only, even on POSIX hosts where C Lua can remove empty directories. It uses `File.Exists`/`File.Delete` and has no directory-removal path. Handled errors use .NET HResult codes. |
| [`os.rename(oldname, newname)`](https://www.lua.org/manual/5.2/manual.html#pdf-os.rename) | Moves/renames a file or directory; reports success or handled failure. | **Host-dependent:** default provider uses `File.Move`/`File.Replace` and `Directory.Move`. Directory replacement restrictions follow .NET/host filesystem behavior rather than C `rename`; .NET Standard 2.1 uses `File.Replace` for an existing file destination. |
| [`os.setlocale(locale [, category])`](https://www.lua.org/manual/5.2/manual.html#pdf-os.setlocale) | Officially selects or queries a C locale. | **Limitation:** only the literal `C` is accepted; other strings return nil, nil is not a locale-query operation, and `category` is ignored. No process C locale is changed; the implementation uses invariant managed formatting and mode-specific string rules. |
| [`os.time([table])`](https://www.lua.org/manual/5.2/manual.html#pdf-os.time) | Without a table, returns current Unix seconds. With a table, converts local date fields; defaults are hour 12, minute/second 0. | **Limitation:** constructs a .NET `DateTime`, so out-of-range fields are rejected rather than normalized by C `mktime`. `isdst` is ignored and the input table is not rewritten with normalized fields. Default current time comes from `TimeProvider`. |
| [`os.tmpname()`](https://www.lua.org/manual/5.2/manual.html#pdf-os.tmpname) | Returns a temporary pathname. | **Host-dependent:** default `Path.GetTempFileName` creates an empty file while choosing the name. The caller owns cleanup; unlike `io.tmpfile`, this API does not attach delete-on-close behavior. The name uses the state's string representation. |

## Date conversion details

The date formatter is a managed mapping, not a complete C `strftime` implementation. The following conversions are recognized:

`%a %A %b %B %c %C %d %D %e %F %g %G %h %H %I %j %m %M %n %p %r %R %S %t %T %u %w %x %X %y %Y %z %Z %%`

Known formatting differences and incomplete conversions include:

- `%U`, `%V`, and `%W` currently emit the literal `??`; week-number calculation is not implemented.
- `%g`/`%G` use the calendar year, not the ISO week-based year defined for those conversions by POSIX `strftime`. Some C Lua builds reject these conversions entirely.
- `%z` and `%Z` both use .NET's `zzz` offset format, including a colon, rather than separate C numeric-offset/timezone-name conversions.
- `%c`, `%x`, `%X`, month/day names, and related patterns use invariant .NET formatting; changing `os.setlocale` has no effect.
- `%E`/`%O` modifiers and unknown conversions raise an error.

Timestamp conversion is bounded by .NET date/time ranges. For byte-string states, format literals are represented as byte-string units; the formatter's generated names come from invariant .NET date strings. Use the official link for the complete Lua/C contract and this list for the implemented behavior.
