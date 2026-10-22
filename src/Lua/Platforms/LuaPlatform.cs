using Lua.IO;
using Lua.Loaders;

namespace Lua.Platforms;

/// <summary>
///  Platform abstraction for Lua.
/// </summary>
/// <param name="FileSystem"></param>
/// <param name="OsEnvironment"></param>
/// <param name="StandardIO"></param>
public record LuaPlatform(
    ILuaFileSystem FileSystem,
    ILuaOsEnvironment OsEnvironment,
    ILuaStandardIO StandardIO,
    TimeProvider TimeProvider
)
{
    /// <summary>
    /// When true, Lua strings are byte strings: every <see cref="char"/> holds one byte (0x00-0xFF),
    /// so <c>#s</c>, <c>string.len</c>, <c>string.sub</c> and <c>string.byte</c> count bytes exactly
    /// like the reference C implementation (UTF-8 text such as "ế" has length 3 instead of 1).
    /// Character classes (<c>%a</c>, <c>%w</c>, ...), <c>string.upper</c> and <c>string.lower</c> follow the
    /// C locale (ASCII only) and <c>string.char</c> rejects values above 255.
    /// The <see cref="FileSystem"/> and <see cref="StandardIO"/> of the platform must read and write
    /// bytes as well, which <see cref="DefaultByteStrings"/> does. The default is false (UTF-16 strings).
    /// Use <see cref="Lua.LuaByteString"/> to convert between .NET strings and byte strings.
    /// </summary>
    public bool ByteStrings { get; init; }

    // Converts a .NET string (file name, environment value, error text) into a Lua string for this platform.
    internal string ToLuaString(string value)
    {
        return ByteStrings ? LuaByteString.FromString(value) : value;
    }

    /// <summary>
    /// Standard console platform implementation.
    /// Uses real file system, console I/O, and system operations.
    /// </summary>
    public static LuaPlatform Default { get; } =
        new(
            FileSystem: new FileSystem(),
            OsEnvironment: new SystemOsEnvironment(),
            StandardIO: new ConsoleStandardIO(),
            TimeProvider: TimeProvider.System
        );

    /// <summary>
    /// Same as <see cref="Default"/>, but with <see cref="ByteStrings"/> enabled: files and standard I/O
    /// are read and written as raw bytes.
    /// </summary>
    public static LuaPlatform DefaultByteStrings { get; } =
        new(
            FileSystem: new FileSystem(byteStrings: true),
            OsEnvironment: new SystemOsEnvironment(),
            StandardIO: new ConsoleStandardIO(byteStrings: true),
            TimeProvider: TimeProvider.System
        )
        {
            ByteStrings = true,
        };
}
