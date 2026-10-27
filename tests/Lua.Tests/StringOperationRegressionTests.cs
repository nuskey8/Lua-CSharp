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
}
