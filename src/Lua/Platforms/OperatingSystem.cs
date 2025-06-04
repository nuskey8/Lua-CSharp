using System.Diagnostics;

namespace Lua.Platforms
{
    /// <summary>
    /// Default implementation of ILuaOperatingSystem
    /// </summary>
    public sealed class OperatingSystem : ILuaOperatingSystem
    {
        public string? GetEnvironmentVariable(string name)
        {
            return Environment.GetEnvironmentVariable(name);
        }

        public void Exit(int exitCode)
        {
            Environment.Exit(exitCode);
        }

        public DateTime GetProcessStartTime()
        {
            return Process.GetCurrentProcess().StartTime;
        }

        public DateTime GetCurrentUtcTime()
        {
            return DateTime.UtcNow;
        }

        public TimeSpan GetLocalTimeZoneOffset()
        {
            return TimeZoneInfo.Local.BaseUtcOffset;
        }
    }
}