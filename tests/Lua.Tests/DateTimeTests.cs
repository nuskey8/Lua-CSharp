using Lua.Platforms;
using Lua.Standard;
using Microsoft.Extensions.Time.Testing;

namespace Lua.Tests;

public class DateTimeTests
{
    [Test]
    public async Task Test_LocalFunction_Nil_1()
    {
        var source = """
                     return os.date("%d-%m-%Y %H:%M:%S");
                     """;
        var timeProvider = new FakeTimeProvider();
        timeProvider.SetUtcNow(new DateTime(2000, 1, 2, 3, 4, 5, DateTimeKind.Utc));
        var state = LuaState.Create(LuaPlatform.Default with { TimeProvider = timeProvider });
        state.OpenOperatingSystemLibrary();
        var result = await state.DoStringAsync(source);

        Assert.That(result, Has.Length.EqualTo(1));
        Assert.That(result[0], Is.EqualTo(new LuaValue("02-01-2000 03:04:05")));
    }

    [Test]
    [TestCase("%")]
    [TestCase("%O")]
    [TestCase("%E")]
    [TestCase("%Ea")]
    public void OsDate_InvalidFormatSpecifier_Throws(string format)
    {
        var timeProvider = new FakeTimeProvider();
        timeProvider.SetUtcNow(new DateTime(2000, 1, 2, 3, 4, 5, DateTimeKind.Utc));
        var state = LuaState.Create(LuaPlatform.Default with { TimeProvider = timeProvider });
        state.OpenOperatingSystemLibrary();

        var exception = Assert.ThrowsAsync<LuaRuntimeException>(async () => await state.DoStringAsync($"return os.date(\"{format}\")"));

        Assert.That(exception, Is.Not.Null);
        Assert.That(exception!.Message, Does.Contain("invalid conversion specifier"));
    }

    [Test]
    public async Task OsDate_WithExplicitTime_UsesThatTime()
    {
        const double unixTime = 946728000; // 2000-01-01 12:00:00 UTC
        var timeProvider = new FakeTimeProvider();
        timeProvider.SetUtcNow(new DateTime(2030, 2, 3, 4, 5, 6, DateTimeKind.Utc));
        var state = LuaState.Create(LuaPlatform.Default with { TimeProvider = timeProvider });
        state.OpenOperatingSystemLibrary();

        var result = await state.DoStringAsync($"return os.date(\"%Y\", {unixTime})");

        Assert.That(result, Has.Length.EqualTo(1));
        Assert.That(result[0], Is.EqualTo(new LuaValue("2000")));
    }

    [Test]
    public async Task OsDateTable_RoundTripsThroughOsTime()
    {
        const double unixTime = 946728000; // 2000-01-01 12:00:00 UTC
        var state = LuaState.Create();
        state.OpenOperatingSystemLibrary();

        var result = await state.DoStringAsync($"""
            local t = {unixTime}
            local D = os.date("*t", t)
            return os.time(D)
            """);

        Assert.That(result, Has.Length.EqualTo(1));
        Assert.That(result[0].Read<double>(), Is.EqualTo(unixTime));
    }

    [Test]
    public async Task OsTime_WithoutArguments_ReturnsWholeSeconds()
    {
        var timeProvider = new FakeTimeProvider();
        timeProvider.SetUtcNow(new DateTime(2000, 1, 2, 3, 4, 5, 678, DateTimeKind.Utc));
        var state = LuaState.Create(LuaPlatform.Default with { TimeProvider = timeProvider });
        state.OpenOperatingSystemLibrary();

        var result = await state.DoStringAsync("""
            local t = os.time()
            local D = os.date("*t", t)
            return t, os.time(D)
            """);

        Assert.That(result, Has.Length.EqualTo(2));
        Assert.That(result[0].Read<double>(), Is.EqualTo(946782245));
        Assert.That(result[1].Read<double>(), Is.EqualTo(result[0].Read<double>()));
    }
}
