using Lua.IO;
using Lua.Loaders;

namespace Lua.Platforms;

/// <summary>
///  Platform abstraction for Lua.
/// </summary>
/// <param name="fileSystem"></param>
/// <param name="osEnvironment"></param>
/// <param name="standardIO"></param>
public sealed class LuaPlatform(ILuaFileSystem fileSystem, ILuaOsEnvironment osEnvironment, ILuaStandardIO standardIO, TimeProvider timeProvider)
{
    /// <summary>
    /// Standard console platform implementation.
    /// Uses real file system, console I/O, and system operations.
    /// </summary>
    public static LuaPlatform Default =>
        new(fileSystem: new FileSystem(),
            osEnvironment: new SystemOsEnvironment(),
            standardIO: new ConsoleStandardIO(),
            timeProvider: TimeProvider.System);

    public ILuaFileSystem FileSystem { get; set; } = fileSystem;
    public ILuaOsEnvironment OsEnvironment { get; set; } = osEnvironment;
    public ILuaStandardIO StandardIO { get; set; } = standardIO;
    public TimeProvider TimeProvider { get; set; } = timeProvider;
}