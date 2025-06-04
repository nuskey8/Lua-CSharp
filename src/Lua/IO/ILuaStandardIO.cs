namespace Lua.IO;

/// <summary>
/// Interface for standard IO operations (stdin, stdout, stderr)
/// </summary>
public interface ILuaStandardIO
{
    /// <summary>
    /// Open standard input stream
    /// </summary>
    ILuaIOStream Input { get; }

    /// <summary>
    /// Open standard output stream
    /// </summary>
    ILuaIOStream Output { get; }

    /// <summary>
    /// Open standard error stream
    /// </summary>
    ILuaIOStream Error { get; }
}