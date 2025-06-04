using Lua.IO;

namespace Lua.Platforms;

/// <summary>
/// Represents a complete platform configuration for Lua execution.
/// Provides all platform-specific implementations in one cohesive package.
/// </summary>
public interface ILuaPlatform
{
    /// <summary>
    /// Gets the file system implementation for this platform
    /// </summary>
    ILuaFileSystem FileSystem { get; }
    
    /// <summary>
    /// Gets the operating system abstraction for this platform
    /// </summary>
    ILuaOperatingSystem OperatingSystem { get; }
    
    /// <summary>
    /// Gets the standard I/O implementation for this platform
    /// </summary>
    ILuaStandardIO StandardIO { get; }
}