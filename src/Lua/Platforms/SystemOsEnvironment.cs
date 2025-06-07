using System.Diagnostics;

namespace Lua.Platforms
{
    /// <summary>
    /// Default implementation of ILuaEnvironment
    /// </summary>
    public sealed class SystemOsEnvironment : ILuaOsEnvironment
    {
        public string? GetEnvironmentVariable(string name)
        {
            return Environment.GetEnvironmentVariable(name);
        }

        public ValueTask Exit(int exitCode, CancellationToken cancellationToken)
        {
            Environment.Exit(exitCode);
            return default;
        }

        public double GetTotalProcessorTime()
        {
            return Process.GetCurrentProcess().TotalProcessorTime.TotalSeconds;
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