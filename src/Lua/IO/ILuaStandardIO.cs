namespace Lua.IO;

/// <summary>
/// Interface for standard IO operations (stdin, stdout, stderr)
/// </summary>
public interface ILuaStandardIO
{
    /// <summary>
    /// Open standard input stream
    /// </summary>
    ILuaStream Input { get; }

    /// <summary>
    /// Open standard output stream
    /// </summary>
    ILuaStream Output { get; }

    /// <summary>
    /// Open standard error stream
    /// </summary>
    ILuaStream Error { get; }
}