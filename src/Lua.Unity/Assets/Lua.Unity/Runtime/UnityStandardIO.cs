using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Lua.IO;

namespace Lua.Unity
{
    public class UnityStandardIO : ILuaStandardIO
    {
        public UnityStandardIO(ILuaStream input = null)
            : this(LuaStringMode.Utf16, input) { }

        public UnityStandardIO(LuaStringMode stringMode, ILuaStream input = null)
        {
            Input = input ?? new DummyInputStream();
            Output = new DebugLogStream(false, stringMode);
            Error = new DebugLogStream(true, stringMode);
        }

        public ILuaStream Input { get; }
        public ILuaStream Output { get; }
        public ILuaStream Error { get; }
    }

    internal class DummyInputStream : ILuaStream
    {
        public bool IsOpen { get; } = true;
        public LuaFileOpenMode Mode => LuaFileOpenMode.Read;

        public void Dispose() { }
    }

    public class DebugLogStream : ILuaStream
    {
        public DebugLogStream(bool isError = false)
            : this(isError, LuaStringMode.Utf16) { }

        public DebugLogStream(bool isError, LuaStringMode stringMode)
        {
            IsError = isError;
            this.stringMode = stringMode;
        }

        public bool IsError { get; } = false;
        public bool IsOpen { get; } = true;
        public LuaFileOpenMode Mode => LuaFileOpenMode.Write;

        private readonly StringBuilder stringBuilder = new();
        private readonly LuaStringMode stringMode;

        ValueTask ILuaStream.WriteAsync(
            ReadOnlyMemory<char> content,
            CancellationToken cancellationToken
        )
        {
            stringBuilder.Append(content.Span);
            return default;
        }

        ValueTask ILuaStream.FlushAsync(CancellationToken cancellationToken)
        {
            if (stringBuilder.Length > 0)
            {
                var message = stringBuilder.ToString();
                if (stringMode == LuaStringMode.Bytes)
                    message = LuaByteString.ToString(message);
                if (IsError)
                    UnityEngine.Debug.LogError(message);
                else
                    UnityEngine.Debug.Log(message);
                stringBuilder.Clear();
            }

            return default;
        }

        public ValueTask Close(CancellationToken cancellationToken)
        {
            throw new NotSupportedException("DebugLogStream cannot be closed.");
        }

        public void Dispose() { }
    }
}
