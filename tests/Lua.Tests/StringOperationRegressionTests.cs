using Lua.Platforms;
using Lua.Standard;

namespace Lua.Tests;

[TestFixture(LuaStringMode.Utf16)]
[TestFixture(LuaStringMode.Bytes)]
public class StringOperationRegressionTests(LuaStringMode stringMode)
{
    LuaState CreateState()
    {
        var state = LuaState.Create(
            stringMode == LuaStringMode.Bytes ? LuaPlatform.DefaultByteStrings : LuaPlatform.Default
        );
        state.OpenStandardLibraries();
        return state;
    }

    [Test]
    public async Task StringRepetitionBoundaries()
    {
        using var state = CreateState();
        await state.DoStringAsync(
            """
            assert(string.rep('x', -1) == '')
            assert(string.rep('x', 0, ':') == '')
            assert(string.rep('', 1000000000) == '')
            assert(string.rep('', 4, ':') == ':::')
            assert(string.rep('ab', 1, ':') == 'ab')
            assert(string.rep('ab', '3', ':') == 'ab:ab:ab')
            assert(string.rep('a\0b', 3, '\0') == 'a\0b\0a\0b\0a\0b')
            for n = 1, 65 do
                local t = {}
                for i = 1, n do t[i] = 'abc' end
                assert(string.rep('abc', n, '::') == table.concat(t, '::'))
            end
            local ok, message = pcall(string.rep, 'xx', 2147483647, ':')
            assert(not ok and string.find(message, 'resulting string too large', 1, true))
            ok, message = pcall(string.rep, 'x', 2147483648)
            assert(not ok and string.find(message, 'resulting string too large', 1, true))
            assert(not pcall(string.rep, 'x', 0, false))
            """
        );
    }

    [Test]
    public async Task StringTransformsPreserveCodeUnitsAndRanges()
    {
        using var state = CreateState();
        await state.DoStringAsync(
            """
            assert(string.reverse('') == '')
            assert(string.reverse('a') == 'a')
            assert(string.reverse('ab\0cd') == 'dc\0ba')
            local s = string.char(0, 127, 128, 255)
            assert(string.reverse(string.reverse(s)) == s)
            assert(string.sub(s, 1) == s)
            assert(string.sub(s, -100, 100) == s)
            assert(string.sub(s, 2, -2) == string.char(127, 128))
            assert(string.sub(s, 5) == '')
            assert(string.sub(s, 3, 1) == '')
            """
        );
    }

    [Test]
    public async Task FractionalRepeatCountsTruncateTowardZero()
    {
        using var state = CreateState();
        await state.DoStringAsync(
            """
            assert(string.rep('ab', 2.9, ':') == 'ab:ab')
            assert(string.rep('ab', '2.9', ':') == 'ab:ab')
            assert(string.rep('ab', 1.9, ':') == 'ab')
            assert(string.rep('ab', 0.9, ':') == '')
            assert(string.rep('ab', -0.9, ':') == '')
            assert(string.rep('ab', -2.9, ':') == '')
            assert(string.rep('', 2.9, ':') == ':')
            assert(string.rep('a\0b', 2.9, '\0') == 'a\0b\0a\0b')
            assert(not pcall(string.rep, 'ab', 0.9, false))
            assert(not pcall(string.rep, 'ab', {}))
            assert(not pcall(string.rep, 'ab', 0/0))
            assert(not pcall(string.rep, 'ab', math.huge))
            assert(not pcall(string.rep, 'ab', -math.huge))
            local ok, message = pcall(string.rep, 'xx', 2147483647.5, ':')
            assert(not ok and string.find(message, 'resulting string too large', 1, true))
            """
        );
    }
}
