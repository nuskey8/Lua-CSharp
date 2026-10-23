using System.Text;
using Lua.IO;
using Lua.Platforms;
using Lua.Standard;

namespace Lua.Tests;

// Expected values in this file were produced by the reference Lua 5.2.4 interpreter.
public sealed class ByteStringTests : IDisposable
{
    // VI is "Tien Len" with accents (Vietnamese) as UTF-8 bytes; the sources use \x escapes so this file stays ASCII.
    const string Prelude = """
        local VI = "Ti\xE1\xBA\xBFn L\xC3\xAAn"
        local E = "\xE1\xBA\xBF"
        local function hex(s) return (s:gsub(".", function(c) return string.format("%02x", c:byte()) end)) end

        """;

    readonly string testDirectory = Path.Combine(
        Path.GetTempPath(),
        $"LuaByteStringTests_{Guid.NewGuid()}"
    );

    public ByteStringTests()
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

    LuaState CreateByteState()
    {
        var state = LuaState.Create(
            LuaPlatform.DefaultByteStrings with
            {
                FileSystem = new FileSystem(testDirectory, LuaStringMode.Bytes),
            }
        );
        state.OpenStandardLibraries();
        return state;
    }

    async Task RunAsync(string body)
    {
        using var state = CreateByteState();
        await state.DoStringAsync(Prelude + body);
    }

    [Test]
    public void DefaultPlatform_KeepsUtf16Strings()
    {
        Assert.That(LuaPlatform.Default.StringMode, Is.EqualTo(LuaStringMode.Utf16));
        Assert.That(LuaPlatform.DefaultByteStrings.StringMode, Is.EqualTo(LuaStringMode.Bytes));
    }

    [Test]
    public async Task DefaultMode_StringLenCountsUtf16Chars()
    {
        var state = LuaState.Create();
        state.OpenStandardLibraries();
        var result = await state.DoStringAsync(
            "return string.len('\u3042\u3044\u3046\u3048\u304A'), #'\u1EBF', ('\u1EBF'):find('%a')"
        );

        Assert.That(result[0], Is.EqualTo(new LuaValue(5)));
        Assert.That(result[1], Is.EqualTo(new LuaValue(1)));
        Assert.That(result[2], Is.EqualTo(new LuaValue(1)));
    }

    [Test]
    public Task Length_Byte_Char_Sub() =>
        RunAsync(
            """
            assert(#VI == 11 and VI:len() == 11)
            assert(#E == 3)
            assert(#"\xE3\x81\x82\xE3\x81\x84\xE3\x81\x86\xE3\x81\x88\xE3\x81\x8A" == 15)
            assert(#"\xF0\x9F\x98\x80" == 4)
            assert(select("#", E:byte(1, -1)) == 3)
            local a, b, c = E:byte(1, -1)
            assert(a == 225 and b == 186 and c == 191)
            assert(string.char(225, 186, 191) == E)
            assert(hex(string.char(255, 0, 128)) == "ff0080")
            assert(hex(VI:sub(3, 4)) == "e1ba")
            assert(hex(VI:sub(-3)) == "c3aa6e")
            assert(hex(E:sub(1, 1)) == "e1" and hex(E:sub(2)) == "babf")
            assert(hex(E:reverse()) == "bfbae1")
            assert(hex(E:rep(2, "|")) == "e1babf7ce1babf")
            assert(hex(E .. "-" .. E) == "e1babf2de1babf")
            """
        );

    [Test]
    public Task StringChar_RejectsValuesAboveByteRange() =>
        RunAsync(
            """
            assert(not pcall(string.char, 256))
            assert(not pcall(string.char, -1))
            assert(not pcall(string.char, 65 + 256))
            local ok, err = pcall(string.char, 256)
            assert(err:find("value out of range", 1, true))
            assert(hex(string.format("%c", 256 + 65)) == "41")
            assert(hex(string.format("%c%c%c", 225, 186, 191)) == "e1babf")
            """
        );

    [Test]
    public Task UpperLower_OnlyChangeAscii() =>
        RunAsync(
            """
            assert(hex(("a\xC3\xA0b\xE1\xBA\xBF"):upper()) == "41c3a042e1babf")
            assert(hex(("A\xC3\x80B\xE1\xBA\xBE"):lower()) == "61c38062e1babe")
            local all = {}
            for c = 0, 255 do all[#all + 1] = string.char(c) end
            all = table.concat(all)
            local up, low = all:upper(), all:lower()
            assert(#up == 256 and #low == 256)
            for c = 0, 255 do
              local ch = string.char(c)
              local wantUp = (c >= 97 and c <= 122) and c - 32 or c
              local wantLow = (c >= 65 and c <= 90) and c + 32 or c
              assert(up:byte(c + 1) == wantUp, "upper " .. c)
              assert(low:byte(c + 1) == wantLow, "lower " .. c)
            end
            """
        );

    // C locale ctype for every byte, written independently of the implementation.
    static bool CType(char cls, int c)
    {
        var upper = c >= 'A' && c <= 'Z';
        var lower = c >= 'a' && c <= 'z';
        var digit = c >= '0' && c <= '9';
        return cls switch
        {
            'a' => upper || lower,
            'c' => c < 32 || c == 127,
            'd' => digit,
            'g' => c > 32 && c < 127,
            'l' => lower,
            'p' => c > 32 && c < 127 && !(upper || lower || digit),
            's' => c == 32 || (c >= 9 && c <= 13),
            'u' => upper,
            'w' => upper || lower || digit,
            'x' => digit || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F'),
            _ => throw new ArgumentOutOfRangeException(nameof(cls)),
        };
    }

    [TestCase('a')]
    [TestCase('c')]
    [TestCase('d')]
    [TestCase('g')]
    [TestCase('l')]
    [TestCase('p')]
    [TestCase('s')]
    [TestCase('u')]
    [TestCase('w')]
    [TestCase('x')]
    public async Task CharacterClasses_FollowCLocaleForEveryByte(char cls)
    {
        var expected = new StringBuilder();
        var expectedComplement = new StringBuilder();
        for (var c = 0; c < 256; c++)
        {
            expected.Append(CType(cls, c) ? '1' : '0');
            expectedComplement.Append(CType(cls, c) ? '0' : '1');
        }

        var upperCls = char.ToUpperInvariant(cls);
        using var state = CreateByteState();
        var result = await state.DoStringAsync(
            $$"""
            local function bits(pattern)
              local t = {}
              for c = 0, 255 do t[#t + 1] = string.char(c):find(pattern) and "1" or "0" end
              return table.concat(t)
            end
            return bits("%{{cls}}"), bits("%{{upperCls}}"), bits("[%{{cls}}]"), bits("[^%{{cls}}]")
            """
        );

        Assert.That(result[0].Read<string>(), Is.EqualTo(expected.ToString()));
        Assert.That(result[1].Read<string>(), Is.EqualTo(expectedComplement.ToString()));
        Assert.That(result[2].Read<string>(), Is.EqualTo(expected.ToString()));
        Assert.That(result[3].Read<string>(), Is.EqualTo(expectedComplement.ToString()));
    }

    [Test]
    public Task Patterns_WorkOnBytes() =>
        RunAsync(
            """
            local s, n = VI:gsub("%a+", "X")
            assert(hex(s) == hex("X\xE1\xBA\xBFX X\xC3\xAAX") and n == 4)
            assert(select(2, VI:gsub("%a+", "X")) == 4)
            assert(E:find("%a") == nil)
            assert(VI:find(E, 1, true) == 3)
            assert(VI:find("L") == 8)
            assert(VI:find("n", 7) == 11)
            local c1, c2 = VI:match("^(.)(.)")
            assert(hex(c1) == "54" and hex(c2) == "69")
            assert(VI:match("()L") == 8)
            assert(hex((E:gsub(".", "<%0>"))) == "3ce13e3cba3e3cbf3e")
            local words = {}
            for w in VI:gmatch("%S+") do words[#words + 1] = hex(w) end
            assert(table.concat(words, ",") == "5469e1babf6e,4cc3aa6e")
            local bytes = {}
            for ch in E:gmatch(".") do bytes[#bytes + 1] = hex(ch) end
            assert(table.concat(bytes, ",") == "e1,ba,bf")
            assert(VI:gsub("%f[%a]%a+", "<%0>") == "<Ti>\xE1\xBA\xBF<n> <L>\xC3\xAA<n>")
            assert(("(\xE1\xBA\xBF (x)) y"):match("%b()") == "(\xE1\xBA\xBF (x))")
            """
        );

    [Test]
    public Task Format_PadsByBytes() =>
        RunAsync(
            """
            assert(hex(string.format("%5s|%-5s|", E, E)) == "2020e1babf7c" .. "e1babf20207c")
            assert(hex(string.format("%.2s", E)) == "e1ba")
            assert(#string.format("%s", VI) == 11)
            """
        );

    [Test]
    public Task Comparison_IsByteWise() =>
        RunAsync(
            """
            assert("\x80" > "\x7F" and "\xFF" > "\x01" and "\x7F" < "\x80")
            local w = { "\xC3\xA9", "e", "z", "\xE1\xBA\xBF", "a", "\xC3\x80", "Z", "\xC3\xAA" }
            table.sort(w)
            local h = {}
            for i, x in ipairs(w) do h[i] = hex(x) end
            assert(table.concat(h, ",") == "5a,61,65,7a,c380,c3a9,c3aa,e1babf")
            """
        );

    [Test]
    public async Task DoString_EncodesDotNetSourceAsUtf8()
    {
        using var state = CreateByteState();
        var source = "return 'Ti\u1EBFn', #'Ti\u1EBFn', '\u1EBF'";
        var result = await state.DoStringAsync(source);

        Assert.That(result[0].Read<string>(), Is.EqualTo(LuaByteString.FromString("Ti\u1EBFn")));
        Assert.That(result[1], Is.EqualTo(new LuaValue(6)));
        Assert.That(
            LuaByteString.ToBytes(result[2].Read<string>()),
            Is.EqualTo(new byte[] { 0xE1, 0xBA, 0xBF })
        );
    }

    [Test]
    public async Task LoadBytes_SkipsOnlyUtf8Bom()
    {
        using var state = CreateByteState();
        var withBom = new byte[] { 0xEF, 0xBB, 0xBF }
            .Concat(Encoding.UTF8.GetBytes("return '\u1EBF', #'\u1EBF'"))
            .ToArray();
        var result = await state.ExecuteAsync(state.Load(withBom, "bom", "t"));

        Assert.That(
            LuaByteString.ToBytes(result[0].Read<string>()),
            Is.EqualTo(new byte[] { 0xE1, 0xBA, 0xBF })
        );
        Assert.That(result[1], Is.EqualTo(new LuaValue(3)));
    }

    [Test]
    public async Task LoadFile_ReadsUtf8SourceAsBytes()
    {
        await File.WriteAllBytesAsync(
            Path.Combine(testDirectory, "chunk.lua"),
            new byte[] { 0xEF, 0xBB, 0xBF }
                .Concat(Encoding.UTF8.GetBytes("return '\u1EBF', #'\u1EBF'"))
                .ToArray()
        );

        using var state = CreateByteState();
        var closure = await state.LoadFileAsync("chunk.lua", "bt", null, CancellationToken.None);
        var result = await state.ExecuteAsync(closure);

        Assert.That(
            LuaByteString.ToBytes(result[0].Read<string>()),
            Is.EqualTo(new byte[] { 0xE1, 0xBA, 0xBF })
        );
        Assert.That(result[1], Is.EqualTo(new LuaValue(3)));
    }

    [Test]
    public async Task Require_LoadsUtf8ModuleAsBytes()
    {
        await File.WriteAllBytesAsync(
            Path.Combine(testDirectory, "viet.lua"),
            Encoding.UTF8.GetBytes("return { s = 'Ti\u1EBFn L\u00EAn' }")
        );

        using var state = CreateByteState();
        var result = await state.DoStringAsync(
            $$"""
            package.path = "{{testDirectory.Replace("\\", "/")}}/?.lua"
            local m = require("viet")
            return #m.s
            """
        );

        Assert.That(result[0], Is.EqualTo(new LuaValue(11)));
    }

    [Test]
    public async Task Io_WritesAndReadsRawBytes()
    {
        var data = new byte[]
        {
            0x54,
            0x69,
            0xE1,
            0xBA,
            0xBF,
            0x6E,
            0x0A,
            0xE1,
            0xBA,
            0xBF,
            0xFF,
            0x00,
            0x80,
            0x7A,
            0x0D,
            0x0A,
            0x65,
            0x6E,
            0x64,
        };
        using var state = CreateByteState();
        state.Environment["data"] = LuaByteString.FromBytes(data);
        var result = await state.DoStringAsync(
            """
            local f = assert(io.open("raw.bin", "wb"))
            f:write(data)
            f:close()
            f = assert(io.open("raw.bin", "rb"))
            local size = f:seek("end")
            f:seek("set", 0)
            local all = f:read("*a")
            f:close()
            f = assert(io.open("raw.bin", "rb"))
            local l1 = f:read("*l")
            local n2 = f:read(2)
            f:close()
            return size, all == data, #l1, #n2
            """
        );

        Assert.That(File.ReadAllBytes(Path.Combine(testDirectory, "raw.bin")), Is.EqualTo(data));
        Assert.That(result[0], Is.EqualTo(new LuaValue(data.Length)));
        Assert.That(result[1], Is.EqualTo(new LuaValue(true)));
        Assert.That(result[2], Is.EqualTo(new LuaValue(6)));
        Assert.That(result[3], Is.EqualTo(new LuaValue(2)));
    }

    [Test]
    public async Task FileNames_AreDecodedFromUtf8Bytes()
    {
        using var state = CreateByteState();
        var result = await state.DoStringAsync(
            """
            local f = assert(io.open("t\xE1\xBA\xBFn.bin", "wb"))
            f:write("x")
            f:close()
            local g = assert(io.open("r\xC3\xAA.bin", "wb"))
            g:close()
            local renamed = os.rename("r\xC3\xAA.bin", "r2\xC3\xAA.bin")
            local gone = io.open("r\xC3\xAA.bin", "rb") == nil
            local removed = os.remove("r2\xC3\xAA.bin")
            local ok, msg = io.open("missing_\xE1\xBA\xBF.bin", "rb")
            return renamed, gone, removed, ok == nil, type(msg)
            """
        );

        Assert.That(File.Exists(Path.Combine(testDirectory, "t\u1EBFn.bin")), Is.True);
        Assert.That(File.Exists(Path.Combine(testDirectory, "r\u00EA.bin")), Is.False);
        Assert.That(File.Exists(Path.Combine(testDirectory, "r2\u00EA.bin")), Is.False);
        Assert.That(result[0], Is.EqualTo(new LuaValue(true)));
        Assert.That(result[1], Is.EqualTo(new LuaValue(true)));
        Assert.That(result[2], Is.EqualTo(new LuaValue(true)));
        Assert.That(result[3], Is.EqualTo(new LuaValue(true)));
        Assert.That(result[4].Read<string>(), Is.EqualTo("string"));
    }

    [Test]
    public async Task Require_FindsModuleWithNonAsciiName()
    {
        await File.WriteAllTextAsync(Path.Combine(testDirectory, "m\u1EBFd.lua"), "return 'ok'");

        using var state = CreateByteState();
        var result = await state.DoStringAsync(
            $$"""
            package.path = "{{testDirectory.Replace("\\", "/")}}/?.lua"
            return require("m\xE1\xBA\xBFd")
            """
        );

        Assert.That(result[0].Read<string>(), Is.EqualTo("ok"));
    }

    [Test]
    public async Task OsGetenv_ReturnsUtf8Bytes()
    {
        const string name = "LUA_CSHARP_BYTE_STRINGS_TEST";
        Environment.SetEnvironmentVariable(name, "Ti\u1EBFn");
        try
        {
            using var state = CreateByteState();
            var result = await state.DoStringAsync($"local v = os.getenv('{name}'); return #v, v");

            Assert.That(result[0], Is.EqualTo(new LuaValue(6)));
            Assert.That(LuaByteString.ToString(result[1].Read<string>()), Is.EqualTo("Ti\u1EBFn"));
        }
        finally
        {
            Environment.SetEnvironmentVariable(name, null);
        }
    }

    [Test]
    public async Task Io_DoesNotSkipBomOnRead()
    {
        await File.WriteAllBytesAsync(
            Path.Combine(testDirectory, "bom.bin"),
            new byte[] { 0xEF, 0xBB, 0xBF, 0x61, 0x62, 0x63 }
        );

        using var state = CreateByteState();
        var result = await state.DoStringAsync(
            """
            local f = assert(io.open("bom.bin", "rb"))
            local all = f:read("*a")
            f:close()
            f = assert(io.open("bom.bin", "rb"))
            local n3 = f:read(3)
            local rest = f:read("*l")
            f:close()
            return #all, #n3, rest
            """
        );

        Assert.That(result[0], Is.EqualTo(new LuaValue(6)));
        Assert.That(result[1], Is.EqualTo(new LuaValue(3)));
        Assert.That(result[2].Read<string>(), Is.EqualTo("abc"));
    }

    [Test]
    public async Task Io_NumberReadingTreatsHighBytesAsNotWhitespace()
    {
        await File.WriteAllBytesAsync(
            Path.Combine(testDirectory, "num.bin"),
            new byte[]
            {
                0x20,
                0x20,
                0x31,
                0x32,
                0x20,
                0x30,
                0x78,
                0x31,
                0x46,
                0x20,
                0xA0,
                0x37,
                0x0A,
            }
        );

        using var state = CreateByteState();
        var result = await state.DoStringAsync(
            """
            local f = assert(io.open("num.bin", "rb"))
            local a, b, c = f:read("*n", "*n", "*n")
            f:close()
            return a, b, c
            """
        );

        Assert.That(result[0], Is.EqualTo(new LuaValue(12)));
        Assert.That(result[1], Is.EqualTo(new LuaValue(31)));
        Assert.That(result[2], Is.EqualTo(LuaValue.Nil));
    }

    [Test]
    public async Task LuaStream_ByteMode_RoundTripsEveryByte()
    {
        var all = Enumerable.Range(0, 256).Select(i => (byte)i).ToArray();
        using var memory = new MemoryStream();
        using (var writer = new LuaStream(LuaFileOpenMode.Write, memory, LuaStringMode.Bytes))
        {
            await writer.WriteAsync(
                LuaByteString.FromBytes(all).AsMemory(),
                CancellationToken.None
            );
            await writer.FlushAsync(CancellationToken.None);
            Assert.That(memory.ToArray(), Is.EqualTo(all));
        }

        using var reader = new LuaStream(
            LuaFileOpenMode.Read,
            new MemoryStream(all),
            LuaStringMode.Bytes
        );
        var text = await reader.ReadAllAsync(CancellationToken.None);
        Assert.That(LuaByteString.ToBytes(text), Is.EqualTo(all));
    }

    [Test]
    public async Task LuaStream_DefaultMode_StillEncodesUtf8()
    {
        using var memory = new MemoryStream();
        var writer = new LuaStream(LuaFileOpenMode.Write, memory);
        await writer.WriteAsync("\u1EBF".AsMemory(), CancellationToken.None);
        await writer.FlushAsync(CancellationToken.None);

        Assert.That(memory.ToArray(), Is.EqualTo(new byte[] { 0xE1, 0xBA, 0xBF }));
    }

    [Test]
    public async Task LuaStream_ByteMode_ReadCountCountsBytes()
    {
        using var reader = new LuaStream(
            LuaFileOpenMode.Read,
            new MemoryStream(new byte[] { 0xE1, 0xBA, 0xBF, 0x61 }),
            LuaStringMode.Bytes
        );

        var first = await reader.ReadAsync(2, CancellationToken.None);
        var second = await reader.ReadAsync(2, CancellationToken.None);

        Assert.That(LuaByteString.ToBytes(first!), Is.EqualTo(new byte[] { 0xE1, 0xBA }));
        Assert.That(LuaByteString.ToBytes(second!), Is.EqualTo(new byte[] { 0xBF, 0x61 }));
    }

    [Test]
    public void LuaByteString_ConvertsBetweenDotNetStringsAndBytes()
    {
        const string text = "Ti\u1EBFn L\u00EAn \U0001F600";
        var byteString = LuaByteString.FromString(text);

        Assert.That(byteString.Length, Is.EqualTo(Encoding.UTF8.GetByteCount(text)));
        Assert.That(byteString.All(c => c <= 0xFF), Is.True);
        Assert.That(LuaByteString.ToString(byteString), Is.EqualTo(text));
        Assert.That(LuaByteString.ToBytes(byteString), Is.EqualTo(Encoding.UTF8.GetBytes(text)));
        Assert.That(
            LuaByteString.FromBytes(new byte[] { 0xFF, 0x00, 0x80 }),
            Is.EqualTo("\u00FF\0\u0080")
        );
        Assert.That(LuaByteString.ToString("\u00FF"), Is.EqualTo("\uFFFD"));
        Assert.That(LuaByteString.ToBytes("\u1EBF"), Is.EqualTo(new byte[] { (byte)'?' }));
    }
}
