using Lua.IO;
using System;
using System.Buffers.Text;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Lua.Unity
{
    public class UnityStandardIO : ILuaStandardIO
    {
        public UnityStandardIO(ILuaStream input = null)
        {
            if (input != null)
            {
                Input = input;
            }
            else
            {
                Input = new DummyInputStream();
            }
        }

        public ILuaStream Input { get; }
        public ILuaStream Output { get; } = new DebugLogStream(false);
        public ILuaStream Error { get; } = new DebugLogStream(true);
    }

    public class DummyInputStream : ILuaStream
    {
        public bool IsOpen { get; } = true;
        public LuaFileOpenMode Mode => LuaFileOpenMode.Read;

        public void Dispose() { }
    }

    public class DebugLogStream : ILuaStream
    {
        public DebugLogStream(bool isError = false)
        {
            IsError = isError;
        }

        public bool IsError { get; } = false;
        public bool IsOpen { get; } = true;
        public LuaFileOpenMode Mode => LuaFileOpenMode.Write;

        private readonly StringBuilder stringBuilder = new();

        ValueTask ILuaStream.WriteAsync(ReadOnlyMemory<char> content, CancellationToken cancellationToken)
        {
            stringBuilder.Append(content.Span);
            return default;
        }

        ValueTask ILuaStream.FlushAsync(CancellationToken cancellationToken)
        {
            if (stringBuilder.Length > 0)
            {
                var message = stringBuilder.ToString();
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