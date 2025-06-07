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
        public LuaFileMode Mode => LuaFileMode.Read | LuaFileMode.Text;

        public ValueTask<LuaFileContent> ReadAllAsync(CancellationToken cancellationToken)
        {
            // Simulate reading all input from the console
            UnityEngine.Debug.Log("Reading all input (simulated)");
            return new ValueTask<LuaFileContent>(new LuaFileContent("input"));
        }

        public ValueTask<string> ReadLineAsync(CancellationToken cancellationToken)
        {
            // Simulate reading a line from the console
            UnityEngine.Debug.Log("Reading a line (simulated)");
            return new ValueTask<string>(default(string));
        }

        public ValueTask<string> ReadStringAsync(int count, CancellationToken cancellationToken)
        {
            // Simulate reading a specific number of characters from the console
            UnityEngine.Debug.Log($"Reading {count} characters (simulated)");
            return new ValueTask<string>(default(string));
        }

        public void Dispose() { }
    }

    public class DebugLogStream : ILuaStream
    {
        public DebugLogStream(bool isError = false)
        {
            IsError = isError;
        }

        public bool IsError { get; } = false;
        public LuaFileMode Mode => LuaFileMode.WriteText;

        private readonly StringBuilder stringBuilder = new();

        ValueTask ILuaStream.WriteAsync(LuaFileContent content, CancellationToken cancellationToken)
        {
            if (content.Type == LuaFileContentType.Text)
            {
                stringBuilder.Append(content.ReadString());
                return default;
            }
            else if (content.Type == LuaFileContentType.Binary)
            {
                throw new InvalidOperationException("Binary content cannot be written to DebugLogIOStream.");
            }
            else
            {
                throw new InvalidOperationException("Invalid Contents type.");
            }
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

        public ValueTask<string> ReadLineAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask<LuaFileContent> ReadAllAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

        public void SetVBuf(LuaFileBufferingMode mode, int size) => throw new NotSupportedException();
        public long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public void Dispose() { }
    }
}