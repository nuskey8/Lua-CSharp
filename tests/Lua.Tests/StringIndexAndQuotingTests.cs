using Lua.Platforms;
using Lua.Standard;

namespace Lua.Tests;

[TestFixture(LuaStringMode.Utf16)]
[TestFixture(LuaStringMode.Bytes)]
public class StringIndexAndQuotingTests(LuaStringMode mode)
{
    LuaState CreateState()
    {
        var state = LuaState.Create(
            mode == LuaStringMode.Bytes ? LuaPlatform.DefaultByteStrings : LuaPlatform.Default
        );
        state.OpenStandardLibraries();
        return state;
    }

    [TestCaseSource(nameof(ControlCharacters))]
    public async Task QuotedControlCharactersRoundTripWithoutAbsorbingFollowingDigits(int code)
    {
        using var state = CreateState();
        state.Environment["code"] = code;
        await state.DoStringAsync(
            """
            for _,tail in ipairs({'','x','0','1','2','3','4','5','6','7','8','9'}) do
                local s=string.char(code)..tail
                local q=string.format('%q',s)
                assert(assert(load('return '..q))()==s)
                if code~=10 then
                    local escape='\\'..tostring(code)
                    if tail:find('^%d') then escape=string.format('\\%03d',code) end
                    assert(q=='"'..escape..tail..'"')
                end
            end
            """
        );
    }

    static IEnumerable<int> ControlCharacters() => Enumerable.Range(0, 32).Append(127);

    [Test]
    public async Task QuotedPunctuationAndUnicodeKeepTheirContents()
    {
        using var state = CreateState();
        state.Environment["source"] = "a'\"\\\n\r\0\u00e9\u3042";
        await state.DoStringAsync(
            "assert(assert(load('return '..string.format('%q',source)))()==source)"
        );
    }

    [TestCase("1.9", "-1.9", "abc")]
    [TestCase("-2.9", "-1", "bc")]
    [TestCase("-0.5", "2.9", "ab")]
    [TestCase("'2.9'", "nil", "bc")]
    [TestCase("-2147483649", "2147483648", "abc")]
    [TestCase("2147483648", "2147483648", "")]
    [TestCase("1", "-2147483649", "")]
    public async Task StringSlicesTruncateBeforeRelativeIndexing(
        string first,
        string last,
        string expected
    )
    {
        using var state = CreateState();
        var values = await state.DoStringAsync($"return string.sub('abc',{first},{last})");
        Assert.That(values[0].Read<string>(), Is.EqualTo(expected));
    }

    [TestCase("1.9", "2.9", "ab")]
    [TestCase("-2.9", "-1.9", "bc")]
    [TestCase("-2147483649", "2147483648", "abc")]
    [TestCase("2147483648", "2147483648", "")]
    public async Task ByteSlicesHaveTheSameIndexConversion(
        string first,
        string last,
        string expected
    )
    {
        using var state = CreateState();
        var values = await state.DoStringAsync($"return string.byte('abc',{first},{last})");
        Assert.That(
            values.Select(v => v.Read<double>()),
            Is.EqualTo(expected.Select(c => (double)c))
        );
    }

    [TestCase(-0.5, 0)]
    [TestCase(0.5, 0)]
    [TestCase(65.9, 65)]
    [TestCase(255.9, 255)]
    public async Task CharTruncatesBeforeCheckingItsRange(double input, int expected)
    {
        using var state = CreateState();
        state.Environment["input"] = input;
        var values = await state.DoStringAsync("return string.byte(string.char(input))");
        Assert.That(values[0].Read<double>(), Is.EqualTo(expected));
    }

    [Test]
    public async Task CharFailureCanBeFollowedByAnotherCall()
    {
        using var state = CreateState();
        await state.DoStringAsync(
            "local ok,e=pcall(string.char,65,false); assert(not ok and e:find('bad argument #2',1,true)); assert(string.char('65.9')=='A')"
        );
        Assert.That(state.Stack.Count, Is.Zero);
        Assert.That(state.CallStackFrameCount, Is.Zero);
    }
}
