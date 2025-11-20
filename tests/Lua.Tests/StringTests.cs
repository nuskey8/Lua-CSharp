using Lua.Standard;
using System.Globalization;

namespace Lua.Tests;

public class StringTests
{
    [TestCase("\r")]
    [TestCase("\n")]
    [TestCase("\r\n")]
    public async Task Test_ShortString_RealNewLine(string newLine)
    {
        var result = await LuaState.Create().DoStringAsync($"return \"\\{newLine}\"");
        Assert.That(result, Has.Length.EqualTo(1));
        Assert.That(result[0], Is.EqualTo(new LuaValue("\n")));
    }

    [TestCase("fr-FR")]
    public async Task Test_StringFormat_Culture(string newLine)
    {
        var state = LuaState.Create();
        state.OpenBasicLibrary();
        state.OpenStringLibrary();
        var culture = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(newLine);
        await state.DoStringAsync($"""assert(tonumber(string.format("%f", 10.3)) == 10.3)""");
        CultureInfo.CurrentCulture = culture;
    }
}