using Lua.IO;
using System.Text;
using NUnit.Framework;

namespace Lua.Tests;

public class IOTests : IDisposable
{
    private readonly string testDirectory;
    private readonly FileSystem fileSystem;

    public IOTests()
    {
        testDirectory = Path.Combine(Path.GetTempPath(), $"LuaIOTests_{Guid.NewGuid()}");
        Directory.CreateDirectory(testDirectory);
        fileSystem = new();
    }

    public void Dispose()
    {
        if (Directory.Exists(testDirectory))
        {
            Directory.Delete(testDirectory, true);
        }
    }

    private string GetTestFilePath(string filename)
    {
        return Path.Combine(testDirectory, filename);
    }

    [Test]
    public void FileOpenFlags_ParseModeString_Parses_Correctly()
    {
        // Text modes
        Assert.That(LuaFileModeExtensions.ParseModeString("r"), Is.EqualTo(LuaFileMode.ReadText));
        Assert.That(LuaFileModeExtensions.ParseModeString("w"), Is.EqualTo(LuaFileMode.WriteText));
        Assert.That(LuaFileModeExtensions.ParseModeString("a"), Is.EqualTo(LuaFileMode.AppendText));

        // Binary modes
        Assert.That(LuaFileModeExtensions.ParseModeString("rb"), Is.EqualTo(LuaFileMode.ReadBinary));
        Assert.That(LuaFileModeExtensions.ParseModeString("wb"), Is.EqualTo(LuaFileMode.WriteBinary));
        Assert.That(LuaFileModeExtensions.ParseModeString("ab"), Is.EqualTo(LuaFileMode.AppendBinary));

        // Update modes
        Assert.That(LuaFileModeExtensions.ParseModeString("r+"), Is.EqualTo(LuaFileMode.ReadUpdateText));
        Assert.That(LuaFileModeExtensions.ParseModeString("w+"), Is.EqualTo(LuaFileMode.WriteUpdateText));
        Assert.That(LuaFileModeExtensions.ParseModeString("a+"), Is.EqualTo(LuaFileMode.AppendUpdateText));

        // Binary update modes
        Assert.That(LuaFileModeExtensions.ParseModeString("r+b"), Is.EqualTo(LuaFileMode.ReadUpdateBinary));
        Assert.That(LuaFileModeExtensions.ParseModeString("rb+"), Is.EqualTo(LuaFileMode.ReadUpdateBinary));
        Assert.That(LuaFileModeExtensions.ParseModeString("w+b"), Is.EqualTo(LuaFileMode.WriteUpdateBinary));
        Assert.That(LuaFileModeExtensions.ParseModeString("wb+"), Is.EqualTo(LuaFileMode.WriteUpdateBinary));

        // Mixed order modes
        Assert.That(LuaFileModeExtensions.ParseModeString("br"), Is.EqualTo(LuaFileMode.ReadBinary));
        Assert.That(LuaFileModeExtensions.ParseModeString("rb"), Is.EqualTo(LuaFileMode.ReadBinary));
        Assert.That(LuaFileModeExtensions.ParseModeString("tr"), Is.EqualTo(LuaFileMode.ReadText));
        Assert.That(LuaFileModeExtensions.ParseModeString("rt"), Is.EqualTo(LuaFileMode.ReadText));
    }

    [Test]
    public void FileOpenFlags_GetOpenMode_Returns_Correct_Mode()
    {
        Assert.That(LuaFileMode.Read.GetOpenMode(), Is.EqualTo(LuaFileOpenMode.Read));
        Assert.That(LuaFileMode.Write.GetOpenMode(), Is.EqualTo(LuaFileOpenMode.Write));
        Assert.That(LuaFileMode.Append.GetOpenMode(), Is.EqualTo(LuaFileOpenMode.Append));
        Assert.That(LuaFileMode.ReadUpdate.GetOpenMode(), Is.EqualTo(LuaFileOpenMode.ReadWriteOpen));
        Assert.That(LuaFileMode.WriteUpdate.GetOpenMode(), Is.EqualTo(LuaFileOpenMode.ReadWriteCreate));
        Assert.That(LuaFileMode.AppendUpdate.GetOpenMode(), Is.EqualTo(LuaFileOpenMode.ReadAppend));
    }

    [Test]
    public void FileOpenFlags_GetContentType_Returns_Correct_Type()
    {
        Assert.That(LuaFileMode.Read.GetContentType(), Is.EqualTo(LuaFileContentType.Text));
        Assert.That(LuaFileMode.ReadText.GetContentType(), Is.EqualTo(LuaFileContentType.Text));
        Assert.That(LuaFileMode.ReadBinary.GetContentType(), Is.EqualTo(LuaFileContentType.Binary));
        Assert.That(LuaFileMode.WriteBinary.GetContentType(), Is.EqualTo(LuaFileContentType.Binary));
    }

    [Test]
    public async Task TextStream_Write_And_Read_Text()
    {
        var testFile = GetTestFilePath("text_test.txt");
        var testContent = "Hello, World!\nThis is a test.";

        // Write text
        using (var stream = fileSystem.Open(testFile, LuaFileMode.WriteText))
        {
            await stream.WriteAsync(new(testContent), CancellationToken.None);
        }

        // Read text
        using (var stream = fileSystem.Open(testFile, LuaFileMode.ReadText))
        {
            var content = await stream.ReadAllAsync(CancellationToken.None);
            Assert.That(content.Type, Is.EqualTo(LuaFileContentType.Text));
            Assert.That(content.ReadString(), Is.EqualTo(testContent));
        }
    }

    [Test]
    public async Task BinaryStream_Write_And_Read_Bytes()
    {
        var testFile = GetTestFilePath("binary_test.bin");
        var testBytes = new byte[] { 0x00, 0x01, 0x02, 0xFF, 0xFE, 0xFD };

        // Write bytes
        using (var stream = fileSystem.Open(testFile, LuaFileMode.WriteBinary))
        {
            await stream.WriteAsync(new(testBytes), CancellationToken.None);
        }

        // Read bytes
        using (var stream = fileSystem.Open(testFile, LuaFileMode.ReadBinary))
        {
            var content = await stream.ReadAllAsync(CancellationToken.None);
            Assert.That(content.Type, Is.EqualTo(LuaFileContentType.Binary));
            Assert.That(content.ReadBytes().ToArray(), Is.EqualTo(testBytes));
        }
    }

    [Test]
    public  void TextStream_Cannot_Write_Binary_Content()
    {
        var testFile = GetTestFilePath("text_binary_mix.txt");

        using var stream = fileSystem.Open(testFile, LuaFileMode.WriteText);
        var binaryContent = new LuaFileContent(new byte[] { 0x00, 0x01 });

        Assert.ThrowsAsync<InvalidOperationException>(
            async () => await stream.WriteAsync(binaryContent, CancellationToken.None)
        );
    }

    [Test]
    public void BinaryStream_Cannot_Write_Text_Content()
    {
        var testFile = GetTestFilePath("binary_text_mix.bin");

        using var stream = fileSystem.Open(testFile, LuaFileMode.WriteBinary);
        var textContent = new LuaFileContent("Hello");

        Assert.ThrowsAsync<InvalidOperationException>(
            async () => await stream.WriteAsync(textContent, CancellationToken.None)
        );
    }

    [Test]
    public async Task TextStream_ReadLine_Works()
    {
        var testFile = GetTestFilePath("multiline.txt");
        var lines = new[] { "Line 1", "Line 2", "Line 3" };

        // Write multiple lines
        using (var stream = fileSystem.Open(testFile, LuaFileMode.WriteText))
        {
            await stream.WriteAsync(new(string.Join("\n", lines)), CancellationToken.None);
        }

        // Read lines one by one
        using (var stream = fileSystem.Open(testFile, LuaFileMode.ReadText))
        {
            for (int i = 0; i < lines.Length; i++)
            {
                var line = await stream.ReadLineAsync(CancellationToken.None);
                Assert.That(line, Is.EqualTo(lines[i]));
            }

            // EOF should return null
            var eofLine = await stream.ReadLineAsync(CancellationToken.None);
            Assert.That(eofLine, Is.Null);
        }
    }

    [Test]
    public async Task TextStream_ReadString_Works()
    {
        var testFile = GetTestFilePath("read_string.txt");
        var testContent = "Hello, World!";

        // Write content
        using (var stream = fileSystem.Open(testFile, LuaFileMode.WriteText))
        {
            await stream.WriteAsync(new(testContent), CancellationToken.None);
        }

        // Read partial strings
        using (var stream = fileSystem.Open(testFile, LuaFileMode.ReadText))
        {
            var part1 = await stream.ReadStringAsync(5, CancellationToken.None);
            Assert.That(part1, Is.EqualTo("Hello"));

            var part2 = await stream.ReadStringAsync(7, CancellationToken.None);
            Assert.That(part2, Is.EqualTo(", World"));

            var part3 = await stream.ReadStringAsync(1, CancellationToken.None);
            Assert.That(part3, Is.EqualTo("!")); // Only 1 char left

            var eof = await stream.ReadStringAsync(10, CancellationToken.None);
            Assert.That(eof, Is.Null);
        }
    }

    [Test]
    public async Task BinaryStream_Cannot_Use_Text_Operations()
    {
        var testFile = GetTestFilePath("binary_no_text.bin");

        using (var stream = fileSystem.Open(testFile, LuaFileMode.WriteBinary))
        {
            await stream.WriteAsync(new(new byte[] { 0x01, 0x02 }), CancellationToken.None);
        }

        using (var stream = fileSystem.Open(testFile, LuaFileMode.ReadBinary))
        {
            Assert.ThrowsAsync<InvalidOperationException>(
                async () => await stream.ReadLineAsync(CancellationToken.None)
            );

            Assert.ThrowsAsync<InvalidOperationException>(
                async () => await stream.ReadStringAsync(10, CancellationToken.None)
            );
        }
    }

    [Test]
    public async Task Append_Mode_Appends_Content()
    {
        var testFile = GetTestFilePath("append_test.txt");

        // Write initial content
        using (var stream = fileSystem.Open(testFile, LuaFileMode.WriteText))
        {
            await stream.WriteAsync(new("Hello"), CancellationToken.None);
        }

        // Append content
        using (var stream = fileSystem.Open(testFile, LuaFileMode.AppendText))
        {
            await stream.WriteAsync(new(" World"), CancellationToken.None);
        }

        // Read and verify
        using (var stream = fileSystem.Open(testFile, LuaFileMode.ReadText))
        {
            var content = await stream.ReadAllAsync(CancellationToken.None);
            Assert.That(content.ReadString(), Is.EqualTo("Hello World"));
        }
    }

    [Test]
    public async Task Seek_Works_Correctly()
    {
        var testFile = GetTestFilePath("seek_test.txt");
        var testContent = "0123456789";

        // Write content
        using (var stream = fileSystem.Open(testFile, LuaFileMode.WriteText))
        {
            await stream.WriteAsync(new(testContent), CancellationToken.None);
        }

        // Test seeking
        using (var stream = fileSystem.Open(testFile, LuaFileMode.ReadText))
        {
            // Seek from beginning
            stream.Seek(5, SeekOrigin.Begin);
            var afterBegin = await stream.ReadStringAsync(3, CancellationToken.None);
            Assert.That(afterBegin, Is.EqualTo("567"));

            // Seek from current
            stream.Seek(-2, SeekOrigin.Current);
            var afterCurrent = await stream.ReadStringAsync(2, CancellationToken.None);
            Assert.That(afterCurrent, Is.EqualTo("67"));

            // Seek from end
            stream.Seek(-3, SeekOrigin.End);
            var afterEnd = await stream.ReadStringAsync(3, CancellationToken.None);
            Assert.That(afterEnd, Is.EqualTo("789"));
        }
    }

    [Test]
    public void FileSystem_Rename_Works()
    {
        var oldPath = GetTestFilePath("old_name.txt");
        var newPath = GetTestFilePath("new_name.txt");

        File.WriteAllText(oldPath, "test content");

        fileSystem.Rename(oldPath, newPath);

        Assert.That(File.Exists(oldPath), Is.False);
        Assert.That(File.Exists(newPath), Is.True);
        Assert.That(File.ReadAllText(newPath), Is.EqualTo("test content"));
    }

    [Test]
    public void FileSystem_Remove_Works()
    {
        var testFile = GetTestFilePath("remove_test.txt");

        File.WriteAllText(testFile, "test content");
        Assert.That(File.Exists(testFile), Is.True);

        fileSystem.Remove(testFile);

        Assert.That(File.Exists(testFile), Is.False);
    }

    [Test]
    public void FileSystem_IsReadable_Works()
    {
        var existingFile = GetTestFilePath("readable.txt");
        var nonExistentFile = GetTestFilePath("non_existent.txt");

        File.WriteAllText(existingFile, "test");

        Assert.That(fileSystem.IsReadable(existingFile), Is.True);
        Assert.That(fileSystem.IsReadable(nonExistentFile), Is.False);
    }

    [Test]
    public async Task FileSystem_TempFile_Works()
    {
        string? tempPath = null;

        try
        {
            using (var tempStream = fileSystem.OpenTempFileStream())
            {
                await tempStream.WriteAsync(new("temp content"), CancellationToken.None);

                // Seek and read
                tempStream.Seek(0, SeekOrigin.Begin);
                var content = await tempStream.ReadAllAsync(CancellationToken.None);
                Assert.That(content.ReadString(), Is.EqualTo("temp content"));
            }
        }
        finally
        {
            if (tempPath != null && File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }

    [Test]
    public void FileSystem_DirectorySeparator_IsValid()
    {
        var separator = fileSystem.DirectorySeparator;
        Assert.That(separator, Is.Not.Null);
        Assert.That(separator, Is.Not.Empty);
        Assert.That(separator, Is.EqualTo(Path.DirectorySeparatorChar.ToString()));
    }

    [Test]
    public async Task Buffering_Modes_Work()
    {
        var testFile = GetTestFilePath("buffer_test.txt");

        using (var stream = fileSystem.Open(testFile, LuaFileMode.WriteText))
        {
            // Set no buffering
            stream.SetVBuf(LuaFileBufferingMode.NoBuffering, 0);
            await stream.WriteAsync(new("No buffer"), CancellationToken.None);

            // Set line buffering
            stream.SetVBuf(LuaFileBufferingMode.LineBuffering, 1024);
            await stream.WriteAsync(new("\nLine buffer"), CancellationToken.None);

            // Set full buffering
            stream.SetVBuf(LuaFileBufferingMode.FullBuffering, 4096);
            await stream.WriteAsync(new("\nFull buffer"), CancellationToken.None);

            // Explicit flush
            await stream.FlushAsync(CancellationToken.None);
        }

        // Verify content was written
        var writtenContent = File.ReadAllText(testFile);
        Assert.That(writtenContent, Does.Contain("No buffer"));
        Assert.That(writtenContent, Does.Contain("Line buffer"));
        Assert.That(writtenContent, Does.Contain("Full buffer"));
    }

    [Test]
    public async Task LuaFileContent_Memory_Variations()
    {
        var testFile = GetTestFilePath("memory_test.txt");

        // Test with char array
        var charArray = "Hello from char array".ToCharArray();
        using (var stream = fileSystem.Open(testFile, LuaFileMode.WriteText))
        {
            await stream.WriteAsync(new(charArray), CancellationToken.None);
        }

        using (var stream = fileSystem.Open(testFile, LuaFileMode.ReadText))
        {
            var content = await stream.ReadAllAsync(CancellationToken.None);
            Assert.That(content.ReadString(), Is.EqualTo("Hello from char array"));
        }

        // Test with partial char array
        var longCharArray = "Hello World!!!".ToCharArray();
        using (var stream = fileSystem.Open(testFile, LuaFileMode.WriteText))
        {
            await stream.WriteAsync(new(longCharArray.AsMemory(0, 11)), CancellationToken.None); // Only "Hello World"
        }

        using (var stream = fileSystem.Open(testFile, LuaFileMode.ReadText))
        {
            var content = await stream.ReadAllAsync(CancellationToken.None);
            Assert.That(content.ReadString(), Is.EqualTo("Hello World"));
        }
    }
}