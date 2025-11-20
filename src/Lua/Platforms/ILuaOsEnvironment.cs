namespace Lua.Platforms;

/// <summary>
/// Interface for operating system operations beyond file system
/// </summary>
public interface ILuaOsEnvironment
{
    /// <summary>
    /// Get environment variable value
    /// </summary>
    string? GetEnvironmentVariable(string name);

    /// <summary>
    /// Exit the application with specified code
    /// </summary>
    ValueTask Exit(int exitCode, CancellationToken cancellationToken);

    /// <summary>
    /// Get current process start time for clock calculations (units: seconds)
    /// </summary>
    double GetTotalProcessorTime();
}