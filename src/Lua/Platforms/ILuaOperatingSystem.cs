namespace Lua.Platforms;

/// <summary>
/// Interface for operating system operations beyond file system
/// </summary>
public interface ILuaOperatingSystem
{
    /// <summary>
    /// Get environment variable value
    /// </summary>
    string? GetEnvironmentVariable(string name);

    /// <summary>
    /// Exit the application with specified code
    /// </summary>
    void Exit(int exitCode);

    /// <summary>
    /// Get current process start time for clock calculations
    /// </summary>
    DateTime GetProcessStartTime();

    /// <summary>
    /// Get current UTC time
    /// </summary>
    DateTime GetCurrentUtcTime();

    /// <summary>
    /// Get local time zone offset from UTC
    /// </summary>
    TimeSpan GetLocalTimeZoneOffset();
}