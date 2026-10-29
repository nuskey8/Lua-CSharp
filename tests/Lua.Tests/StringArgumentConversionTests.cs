using System.Globalization;
using Lua.Platforms;
using Lua.Standard;

namespace Lua.Tests;

[TestFixture(LuaNumberFormat.DotNet)]
[TestFixture(LuaNumberFormat.Lua)]
public class StringArgumentConversionTests(LuaNumberFormat format)
{
    LuaState CreateState()
    {
        var state = LuaState.Create(LuaPlatform.Default with { NumberFormat = format });
        state.OpenStandardLibraries();
        return state;
    }

    [Test]
    public async Task NumericStringArgumentsWorkAcrossStringFunctions()
    {
        using var state = CreateState();
        var result = await state.DoStringAsync(
            """
            local first,last=string.find(12345,23)
            local iterator=string.gmatch(123,'%d')
            return string.byte(123,2),string.format(123),string.len(123),
                   string.lower(123),string.upper(123),string.reverse(123),
                   string.sub(12345,2,4),string.rep(12,2,3),string.match(12345,23),
                   (string.gsub(12345,23,0)),first,last,iterator(),iterator(),iterator(),
                   table.concat({'a','b'},12)
            """
        );
        Assert.That(
            result,
            Is.EqualTo(
                new LuaValue[]
                {
                    50,
                    "123",
                    3,
                    "123",
                    "123",
                    "321",
                    "234",
                    "12312",
                    "23",
                    "1045",
                    2,
                    3,
                    "1",
                    "2",
                    "3",
                    "a12b",
                }
            )
        );
    }

    [TestCase(1d / 3)]
    [TestCase(-0d)]
    [TestCase(double.PositiveInfinity)]
    [TestCase(double.NaN)]
    public async Task NumericStringArgumentsUseTheSelectedInvariantFormat(double number)
    {
        using var state = CreateState();
        state.Environment["number"] = number;
        var expected = (await state.DoStringAsync("return tostring(number)"))[0].Read<string>();
        var previousCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            var result = await state.DoStringAsync(
                "return string.len(number),string.sub(number,1),string.format(number),string.match(number,'.+'),table.concat({'a','b'},number)"
            );
            Assert.That(
                result,
                Is.EqualTo(
                    new LuaValue[]
                    {
                        expected.Length,
                        expected,
                        expected,
                        expected,
                        "a" + expected + "b",
                    }
                )
            );
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
        }
    }

    [TestCase("nil")]
    [TestCase("true")]
    [TestCase("{}")]
    [TestCase("setmetatable({}, {__tostring=function() error('unexpected tostring') end})")]
    public async Task StringArgumentsRejectOtherTypesWithoutCallingTostring(string expression)
    {
        using var state = CreateState();
        var result = await state.DoStringAsync($"return pcall(string.len,{expression})");
        Assert.That(result[0], Is.EqualTo(new LuaValue(false)));
        Assert.That(result[1].Read<string>(), Does.Contain("string expected"));
    }

    [Test]
    public async Task OtherLuaLibraryStringArgumentsAcceptNumbers()
    {
        using var state = CreateState();
        var result = await state.DoStringAsync(
            "package.preload['123']=function() return 42 end;local f=assert(load('return 43',123));return require(123),f()"
        );
        Assert.That(result, Is.EqualTo(new LuaValue[] { 42, 43 }));
    }

    [Test]
    public async Task CoercedArgumentsRemainStringsDuringReentrantCallbacks()
    {
        using var state = CreateState();
        var result = await state.DoStringAsync(
            """
            local source,search=123,2
            local subject,pattern
            local replaced=string.gsub(source,search,function()
                local _,value=debug.getlocal(2,1)
                subject=value
                _,value=debug.getlocal(2,2)
                pattern=value
                return 'x'
            end)
            return subject,pattern,replaced,source,search
            """
        );
        Assert.That(result, Is.EqualTo(new LuaValue[] { "123", "2", "1x3", 123, 2 }));
    }
}
