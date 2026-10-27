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
}
