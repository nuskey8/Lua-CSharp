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
    public async Task TextStream_Write_And_Read_Text()
    {
        var testFile = GetTestFilePath("text_test.txt");
        var testContent = "Hello, World!\nThis is a test.";

        // Write text
        using (var stream = await fileSystem.Open(testFile, LuaFileOpenMode.Write, CancellationToken.None))
        {
            await stream.WriteAsync(testContent, CancellationToken.None);
        }

        // Read text
        using (var stream = await fileSystem.Open(testFile, LuaFileOpenMode.Read, CancellationToken.None))
        {
            var content = await stream.ReadAllAsync(CancellationToken.None);
            Assert.That(content, Is.EqualTo(testContent));
        }
    }


    [Test]
    public async Task TextStream_ReadLine_Works()
    {
        var testFile = GetTestFilePath("multiline.txt");
        var lines = new[] { "Line 1", "Line 2", "Line 3" };

        // Write multiple lines
        using (var stream = await fileSystem.Open(testFile, LuaFileOpenMode.Write, CancellationToken.None))
        {
            await stream.WriteAsync((string.Join("\n", lines)), CancellationToken.None);
        }

        // Read lines one by one
        using (var stream = await fileSystem.Open(testFile, LuaFileOpenMode.Read, CancellationToken.None))
        {
            for (int i = 0; i < lines.Length; i++)
            {
                var line = await stream.ReadLineAsync(false, CancellationToken.None);
                Assert.That(line, Is.EqualTo(lines[i]));
            }

            // EOF should return null
            var eofLine = await stream.ReadLineAsync(false, CancellationToken.None);
            Assert.That(eofLine, Is.Null);
        }
    }

    [Test]
    public async Task TextStream_ReadString_Works()
    {
        var testFile = GetTestFilePath("read_string.txt");
        var testContent = "Hello, World!";

        // Write content
        using (var stream = await fileSystem.Open(testFile, LuaFileOpenMode.Write, CancellationToken.None))
        {
            await stream.WriteAsync(testContent, CancellationToken.None);
        }

        // Read partial strings
        using (var stream = await fileSystem.Open(testFile, LuaFileOpenMode.Read, CancellationToken.None))
        {
            var part1 = await stream.ReadAsync(5, CancellationToken.None);
            Assert.That(part1, Is.EqualTo("Hello"));

            var part2 = await stream.ReadAsync(7, CancellationToken.None);
            Assert.That(part2, Is.EqualTo(", World"));

            var part3 = await stream.ReadAsync(1, CancellationToken.None);
            Assert.That(part3, Is.EqualTo("!")); // Only 1 char left

            var eof = await stream.ReadAsync(10, CancellationToken.None);
            Assert.That(eof, Is.Null);
        }
    }


    [Test]
    public async Task Append_Mode_Appends_Content()
    {
        var testFile = GetTestFilePath("append_test.txt");

        // Write initial content
        using (var stream = await fileSystem.Open(testFile, LuaFileOpenMode.Write, CancellationToken.None))
        {
            await stream.WriteAsync(("Hello"), CancellationToken.None);
        }

        // Append content
        using (var stream = await fileSystem.Open(testFile, LuaFileOpenMode.Append, CancellationToken.None))
        {
            await stream.WriteAsync((" World"), CancellationToken.None);
        }

        // Read and verify
        using (var stream = await fileSystem.Open(testFile, LuaFileOpenMode.Read, CancellationToken.None))
        {
            var content = await stream.ReadAllAsync(CancellationToken.None);
            Assert.That(content, Is.EqualTo("Hello World"));
        }
    }

    [Test]
    public async Task Seek_Works_Correctly()
    {
        var testFile = GetTestFilePath("seek_test.txt");
        var testContent = "0123456789";

        // Write content
        using (var stream = await fileSystem.Open(testFile, LuaFileOpenMode.Write, CancellationToken.None))
        {
            await stream.WriteAsync((testContent), CancellationToken.None);
        }

        // Test seeking
        using (var stream = await fileSystem.Open(testFile, LuaFileOpenMode.Read, CancellationToken.None))
        {
            // Seek from beginning
            stream.Seek(SeekOrigin.Begin, 5);
            var afterBegin = await stream.ReadAsync(3, CancellationToken.None);
            Assert.That(afterBegin, Is.EqualTo("567"));

            // Seek from current
            stream.Seek(SeekOrigin.Current, -2);
            var afterCurrent = await stream.ReadAsync(2, CancellationToken.None);
            Assert.That(afterCurrent, Is.EqualTo("67"));

            // Seek from end
            stream.Seek(SeekOrigin.End, -3);
            var afterEnd = await stream.ReadAsync(3, CancellationToken.None);
            Assert.That(afterEnd, Is.EqualTo("789"));
        }
    }

    [Test]
    public async Task FileSystem_Rename_Works()
    {
        var oldPath = GetTestFilePath("old_name.txt");
        var newPath = GetTestFilePath("new_name.txt");

        File.WriteAllText(oldPath, "test content");

        await fileSystem.Rename(oldPath, newPath, CancellationToken.None);

        Assert.That(File.Exists(oldPath), Is.False);
        Assert.That(File.Exists(newPath), Is.True);
        Assert.That(File.ReadAllText(newPath), Is.EqualTo("test content"));
    }

    [Test]
    public async Task FileSystem_Remove_Works()
    {
        var testFile = GetTestFilePath("remove_test.txt");

        File.WriteAllText(testFile, "test content");
        Assert.That(File.Exists(testFile), Is.True);

        await fileSystem.Remove(testFile, CancellationToken.None);

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
            using (var tempStream = await fileSystem.OpenTempFileStream(CancellationToken.None))
            {
                await tempStream.WriteAsync("temp content".AsMemory(), CancellationToken.None);

                // Seek and read
                tempStream.Seek(SeekOrigin.Begin, 0);
                var content = await tempStream.ReadAllAsync(CancellationToken.None);
                Assert.That(content, Is.EqualTo("temp content"));
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

        using (var stream = await fileSystem.Open(testFile, LuaFileOpenMode.Write, CancellationToken.None))
        {
            // Set no buffering
            stream.SetVBuf(LuaFileBufferingMode.NoBuffering, 0);
            await stream.WriteAsync(("No buffer"), CancellationToken.None);

            // Set line buffering
            stream.SetVBuf(LuaFileBufferingMode.LineBuffering, 1024);
            await stream.WriteAsync(("\nLine buffer"), CancellationToken.None);

            // Set full buffering
            stream.SetVBuf(LuaFileBufferingMode.FullBuffering, 4096);
            await stream.WriteAsync(("\nFull buffer"), CancellationToken.None);

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
        using (var stream = await fileSystem.Open(testFile, LuaFileOpenMode.Write, CancellationToken.None))
        {
            await stream.WriteAsync(charArray, CancellationToken.None);
        }

        using (var stream = await fileSystem.Open(testFile, LuaFileOpenMode.Read, CancellationToken.None))
        {
            var content = await stream.ReadAllAsync(CancellationToken.None);
            Assert.That(content, Is.EqualTo("Hello from char array"));
        }

        // Test with partial char array
        var longCharArray = "Hello World!!!".ToCharArray();
        using (var stream = await fileSystem.Open(testFile, LuaFileOpenMode.Write, CancellationToken.None))
        {
            await stream.WriteAsync((longCharArray.AsMemory(0, 11)), CancellationToken.None); // Only "Hello World"
        }

        using (var stream = await fileSystem.Open(testFile, LuaFileOpenMode.Read, CancellationToken.None))
        {
            var content = await stream.ReadAllAsync(CancellationToken.None);
            Assert.That(content, Is.EqualTo("Hello World"));
        }
    }
}