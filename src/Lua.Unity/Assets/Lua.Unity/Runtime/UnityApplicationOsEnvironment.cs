using Lua.Platforms;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace Lua.Unity
{
    public class UnityApplicationOsEnvironment : ILuaOsEnvironment
    {
        public UnityApplicationOsEnvironment(Dictionary<string, string> environmentVariables = null,bool allowToQuitOnExitCall = false)
        {
            EnvironmentVariables = environmentVariables ?? new Dictionary<string, string>();
            AllowToQuitOnExitCall = allowToQuitOnExitCall;
        }


        public bool AllowToQuitOnExitCall { get; }
        public Dictionary<string, string> EnvironmentVariables { get; }

        public string GetEnvironmentVariable(string name)
        {
            if (EnvironmentVariables.TryGetValue(name, out var value))
            {
                return value;
            }

            return null;
        }

        public ValueTask Exit(int exitCode, CancellationToken cancellationToken)
        {
            if (AllowToQuitOnExitCall)
            {
                Application.Quit(exitCode);
                throw new OperationCanceledException();
            }
            else
            {
                // If quitting is not allowed, we can just throw an exception or log a message.
                throw new InvalidOperationException("Application exit is not allowed in this environment.");
            }
        }

        public double GetTotalProcessorTime() => Time.time;


        public DateTime GetCurrentUtcTime() => DateTime.Now;

        public TimeSpan GetLocalTimeZoneOffset() => TimeZoneInfo.Local.BaseUtcOffset;
    }
}