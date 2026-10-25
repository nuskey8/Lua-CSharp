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
    /// In <see cref="LuaStringMode.Bytes"/> mode, every <see cref="char"/> holds one byte (0x00-0xFF),
    /// so <c>#s</c>, <c>string.len</c>, <c>string.sub</c> and <c>string.byte</c> count bytes exactly
    /// like the reference C implementation (UTF-8 text such as "ế" has length 3 instead of 1).
    /// Character classes (<c>%a</c>, <c>%w</c>, ...), <c>string.upper</c> and <c>string.lower</c> follow the
    /// C locale (ASCII only) and <c>string.char</c> rejects values above 255.
    /// The <see cref="FileSystem"/> and <see cref="StandardIO"/> of the platform must read and write
    /// bytes as well, which <see cref="DefaultByteStrings"/> does. The default is <see cref="LuaStringMode.Utf16"/>.
    /// Use <see cref="EncodeText"/> and <see cref="DecodeText"/> to convert .NET text,
    /// or <see cref="LuaByteString"/> for raw bytes.
    /// </summary>
    public LuaStringMode StringMode { get; init; }

    /// <summary>
    /// Converts .NET text to this platform's Lua string representation.
    /// Returns the text unchanged in UTF-16 mode or encodes it as UTF-8 in byte mode.
    /// </summary>
    /// <remarks>Use <see cref="LuaByteString.FromBytes"/> for binary data.</remarks>
    public string EncodeText(string text)
    {
        return StringMode == LuaStringMode.Bytes ? LuaByteString.EncodeUtf8(text) : text;
    }

    /// <summary>
    /// Converts a Lua string in this platform's representation to .NET text.
    /// Returns the string unchanged in UTF-16 mode or decodes its UTF-8 bytes in byte mode.
    /// Invalid UTF-8 is replaced with U+FFFD.
    /// </summary>
    /// <remarks>Use <see cref="LuaByteString.ToBytes"/> for binary data.</remarks>
    public string DecodeText(string luaString)
    {
        return StringMode == LuaStringMode.Bytes ? LuaByteString.DecodeUtf8(luaString) : luaString;
    }

    internal string GetErrorMessage(Exception exception)
    {
        // Lua diagnostics already contain Lua strings (including chunk names).
        // Only host exceptions carry .NET text that needs UTF-8 conversion.
        return exception is LuaRuntimeException or LuaParseException or LuaCompileException
            ? exception.Message
            : EncodeText(exception.Message);
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
    /// Same as <see cref="Default"/>, but with <see cref="StringMode"/> set to <see cref="LuaStringMode.Bytes"/>: files and standard I/O
    /// are read and written as raw bytes.
    /// </summary>
    public static LuaPlatform DefaultByteStrings { get; } =
        new(
            FileSystem: new FileSystem(baseDirectory: null, stringMode: LuaStringMode.Bytes),
            OsEnvironment: new SystemOsEnvironment(),
            StandardIO: new ConsoleStandardIO(LuaStringMode.Bytes),
            TimeProvider: TimeProvider.System
        )
        {
            StringMode = LuaStringMode.Bytes,
        };
}
