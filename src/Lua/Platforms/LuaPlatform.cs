using Lua.IO;
using Lua.Loaders;

namespace Lua.Platforms;

/// <summary>
///  Platform abstraction for Lua.
/// </summary>
/// <param name="FileSystem"></param>
/// <param name="OsEnvironment"></param>
/// <param name="StandardIO"></param>
public sealed record LuaPlatform(ILuaFileSystem FileSystem , ILuaOsEnvironment OsEnvironment,ILuaStandardIO StandardIO): ILuaPlatform
{
    /// <summary>
    /// Standard console platform implementation.
    /// Uses real file system, console I/O, and system operations.
    /// </summary>
    public static  LuaPlatform Default => new( 
        FileSystem: new FileSystem(),
        OsEnvironment: new SystemOsEnvironment(),
        StandardIO:  new ConsoleStandardIO());
}