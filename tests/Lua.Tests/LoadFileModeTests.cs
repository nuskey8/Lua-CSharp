using System.Buffers;
using System.Text;
using Lua.IO;

namespace Lua.Tests;

public sealed class LoadFileModeTests : IDisposable
{
    sealed class NonSeekableByteStream(byte[] bytes) : ILuaStream, ILuaByteStream
    {
        int position;

        public bool IsOpen => true;

        public LuaFileOpenMode Mode => LuaFileOpenMode.Read;

        public ValueTask<string> ReadAllAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var remaining = Encoding.UTF8.GetString(bytes, position, bytes.Length - position);
            position = bytes.Length;
            return new(remaining);
        }

        public ValueTask<double?> ReadNumberAsync(CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public ValueTask<string?> ReadLineAsync(bool keepEol, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public ValueTask<string?> ReadAsync(int count, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public ValueTask WriteAsync(ReadOnlyMemory<char> content, CancellationToken cancellationToken)
        {
            throw new IOException("Stream is read-only");
        }

        public ValueTask ReadBytesAsync(IBufferWriter<byte> writer, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var remainingLength = bytes.Length - position;
            if (remainingLength > 0)
            {
                var buffer = writer.GetSpan(remainingLength);
                bytes.AsSpan(position, remainingLength).CopyTo(buffer);
                writer.Advance(remainingLength);
                position = bytes.Length;
            }

            return default;
        }

        public ValueTask<int> ReadByteAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (position >= bytes.Length)
            {
                return new(-1);
            }

            return new(bytes[position++]);
        }

        public long Seek(SeekOrigin origin, long offset)
        {
            position = origin switch
            {
                SeekOrigin.Begin when offset == 0 => 0,
                _ => throw new NotSupportedException()
            };
            return position;
        }

        public void Dispose()
        {
        }
    }

    sealed class NonSeekableByteFileSystem(byte[] bytes) : Helpers.NotImplementedExceptionFileSystemBase
    {
        public override ValueTask<ILuaStream> Open(string path, LuaFileOpenMode mode, CancellationToken cancellationToken)
        {
            return new((ILuaStream)new NonSeekableByteStream(bytes));
        }
    }

    readonly string testDirectory = Path.Combine(Path.GetTempPath(), $"LuaLoadFileModeTests_{Guid.NewGuid()}");

    public LoadFileModeTests()
    {
        Directory.CreateDirectory(testDirectory);
    }

    public void Dispose()
    {
        if (Directory.Exists(testDirectory))
        {
            Directory.Delete(testDirectory, true);
        }
    }

    LuaState CreateState()
    {
        var state = LuaState.Create();
        state.Platform = state.Platform with { FileSystem = new FileSystem(testDirectory) };
        return state;
    }

    [Test]
    public void LoadFile_BinaryModeRejectsTextChunk()
    {
        File.WriteAllBytes(Path.Combine(testDirectory, "text.lua"), Encoding.UTF8.GetBytes("return 10"));

        using var state = CreateState();

        var exception = Assert.ThrowsAsync<Exception>(async () => await state.LoadFileAsync("text.lua", "b", null, CancellationToken.None));

        Assert.That(exception, Is.Not.Null);
        Assert.That(exception!.Message, Does.Contain("a text chunk"));
    }

    [Test]
    public void LoadFile_TextModeRejectsBinaryChunk()
    {
        File.WriteAllBytes(Path.Combine(testDirectory, "binary.luac"), [0x1B, (byte)' ', (byte)'r', (byte)'e', (byte)'t', (byte)'u', (byte)'r', (byte)'n']);

        using var state = CreateState();

        var exception = Assert.ThrowsAsync<Exception>(async () => await state.LoadFileAsync("binary.luac", "t", null, CancellationToken.None));

        Assert.That(exception, Is.Not.Null);
        Assert.That(exception!.Message, Does.Contain("a binary chunk"));
    }

    [Test]
    public async Task LoadFile_NonSeekableByteStream_TextChunk_DoesNotRequireSeek()
    {
        using var state = LuaState.Create();
        state.Platform = state.Platform with { FileSystem = new NonSeekableByteFileSystem(Encoding.UTF8.GetBytes("return 10")) };

        var closure = await state.LoadFileAsync("text.lua", "bt", null, CancellationToken.None);
        var result = await state.ExecuteAsync(closure);

        Assert.That(result, Has.Length.EqualTo(1));
        Assert.That(result[0], Is.EqualTo(new LuaValue(10)));
    }
}