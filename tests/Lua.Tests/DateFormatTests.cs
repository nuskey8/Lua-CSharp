using Lua.Platforms;
using Lua.Standard;
using Microsoft.Extensions.Time.Testing;

namespace Lua.Tests;

[TestFixture(LuaStringMode.Utf16)]
[TestFixture(LuaStringMode.Bytes)]
public class DateFormatTests(LuaStringMode mode)
{
    LuaState CreateState(FakeTimeProvider provider)
    {
        var platform =
            mode == LuaStringMode.Bytes ? LuaPlatform.DefaultByteStrings : LuaPlatform.Default;
        var state = LuaState.Create(platform with { TimeProvider = provider });
        state.OpenStandardLibraries();
        return state;
    }

    [TestCase(2016, 1, 1, "00 00 53 15 2015")]
    [TestCase(2016, 1, 3, "01 00 53 15 2015")]
    [TestCase(2016, 1, 4, "01 01 01 16 2016")]
    [TestCase(2017, 1, 1, "01 00 52 16 2016")]
    [TestCase(2018, 12, 31, "52 53 01 19 2019")]
    [TestCase(2020, 12, 31, "52 52 53 20 2020")]
    [TestCase(2021, 1, 1, "00 00 53 20 2020")]
    public async Task WeekNumbersAndIsoYearsRespectYearBoundaries(
        int year,
        int month,
        int day,
        string expected
    )
    {
        var provider = new FakeTimeProvider(
            new DateTimeOffset(year, month, day, 12, 0, 0, TimeSpan.Zero)
        );
        using var state = CreateState(provider);
        var result = await state.DoStringAsync("return os.date('!%U %W %V %g %G')");
        Assert.That(result, Is.EqualTo(new LuaValue[] { expected }));
    }

    [Test]
    public async Task CompositeFormatsUseCLocaleFieldsAndPadding()
    {
        var provider = new FakeTimeProvider(new DateTimeOffset(2000, 1, 2, 3, 4, 5, TimeSpan.Zero));
        using var state = CreateState(provider);
        var result = await state.DoStringAsync("return os.date('!%c|%x|%X|%r|%C|%%')");
        Assert.That(
            result,
            Is.EqualTo(
                new LuaValue[] { "Sun Jan  2 03:04:05 2000|01/02/00|03:04:05|03:04:05 AM|20|%" }
            )
        );
    }

    [TestCase(1, "00")]
    [TestCase(999, "09")]
    [TestCase(1000, "10")]
    public async Task CenturyIsPaddedToTwoDigits(int year, string expected)
    {
        var provider = new FakeTimeProvider(new DateTimeOffset(year, 1, 1, 0, 0, 0, TimeSpan.Zero));
        using var state = CreateState(provider);
        var result = await state.DoStringAsync("return os.date('!%C')");
        Assert.That(result, Is.EqualTo(new LuaValue[] { expected }));
    }

    [TestCase(1, "-0800 Test Standard", false, 4)]
    [TestCase(7, "-0700 Test Daylight", true, 5)]
    public async Task LocalAndUtcDatesUseTheConfiguredZoneForCurrentAndExplicitTimes(
        int month,
        string expected,
        bool daylight,
        int hour
    )
    {
        var zone = CreatePacificZone(TimeSpan.FromHours(1));
        var provider = new FakeTimeProvider(
            new DateTimeOffset(2020, month, 15, 12, 0, 0, TimeSpan.Zero)
        );
        provider.SetLocalTimeZone(zone);
        using var state = CreateState(provider);
        state.Environment["timestamp"] = provider.GetUtcNow().ToUnixTimeSeconds();
        var result = await state.DoStringAsync(
            """
            local current=os.date('*t')
            local explicit=os.date('*t',timestamp)
            return os.date('%z %Z'),os.date('%z %Z',timestamp),
                current.isdst,explicit.isdst,current.hour,explicit.hour,
                os.date('!%z %Z'),os.date('!%z %Z',timestamp),os.date('!*t').isdst,
                os.time(current)==timestamp,os.time(explicit)==timestamp
            """
        );
        Assert.That(
            result,
            Is.EqualTo(
                new LuaValue[]
                {
                    expected,
                    expected,
                    daylight,
                    daylight,
                    hour,
                    hour,
                    "+0000 UTC",
                    "+0000 UTC",
                    false,
                    true,
                    true,
                }
            )
        );
    }

    static TimeZoneInfo CreatePacificZone(TimeSpan daylightDelta)
    {
        var rule = TimeZoneInfo.AdjustmentRule.CreateAdjustmentRule(
            new DateTime(2000, 1, 1),
            new DateTime(2099, 12, 31),
            daylightDelta,
            TimeZoneInfo.TransitionTime.CreateFloatingDateRule(
                new DateTime(1, 1, 1, 2, 0, 0),
                3,
                2,
                DayOfWeek.Sunday
            ),
            TimeZoneInfo.TransitionTime.CreateFloatingDateRule(
                new DateTime(1, 1, 1, 2, 0, 0),
                11,
                1,
                DayOfWeek.Sunday
            )
        );
        return TimeZoneInfo.CreateCustomTimeZone(
            "Test/Pacific",
            TimeSpan.FromHours(-8),
            "Test Pacific",
            "Test Standard",
            "Test Daylight",
            [rule]
        );
    }

    [TestCase(1, 11, 1, 8)]
    [TestCase(-1, 3, 8, 9)]
    public async Task RepeatedLocalTimesRoundTripWithTheirDaylightFlag(
        int delta,
        int month,
        int day,
        int hour
    )
    {
        var zone = CreatePacificZone(TimeSpan.FromHours(delta));
        var first = new DateTimeOffset(2020, month, day, hour, 30, 0, TimeSpan.Zero);
        var provider = new FakeTimeProvider(first);
        provider.SetLocalTimeZone(zone);
        using var state = CreateState(provider);
        state.Environment["first"] = first.ToUnixTimeSeconds();
        await state.DoStringAsync(
            """
            local a=os.date('*t',first)
            local b=os.date('*t',first+3600)
            local standard=b.isdst and first or first+3600
            assert(a.hour==b.hour and a.min==b.min and a.isdst~=b.isdst)
            assert(os.time(a)==first and os.time(b)==first+3600)
            assert(os.time(os.date('*t'))==first)
            a.isdst=b.isdst
            assert(os.time(a)==first+3600)
            a.isdst=a.isdst and 0 or false
            assert(os.time(a)==first+3600)
            a.isdst=nil
            assert(os.time(a)==standard)
            """
        );
    }

    [TestCase(-1.9, "1969-12-31 23:59:59", 59)]
    [TestCase(-0.5, "1970-01-01 00:00:00", 0)]
    [TestCase(0.9999999, "1970-01-01 00:00:00", 0)]
    [TestCase(1.9, "1970-01-01 00:00:01", 1)]
    public async Task ExplicitTimestampsTruncateTowardZero(
        double timestamp,
        string expected,
        int second
    )
    {
        using var state = CreateState(new FakeTimeProvider());
        state.Environment["timestamp"] = timestamp;
        var result = await state.DoStringAsync(
            "return os.date('!%Y-%m-%d %H:%M:%S',timestamp),os.date('!*t',timestamp).sec"
        );
        Assert.That(result[0], Is.EqualTo(new LuaValue(expected)));
        Assert.That(result[1].Read<double>(), Is.EqualTo(second));
    }

    [Test]
    public async Task NumericOffsetsIncludeMinutesWithoutAColon()
    {
        var provider = new FakeTimeProvider();
        provider.SetLocalTimeZone(
            TimeZoneInfo.CreateCustomTimeZone(
                "Test/Nepal",
                TimeSpan.FromMinutes(345),
                "Test Nepal",
                "Test Nepal"
            )
        );
        using var state = CreateState(provider);
        var result = await state.DoStringAsync("return os.date('%z')");
        Assert.That(result, Is.EqualTo(new LuaValue[] { "+0545" }));
    }

    [Test]
    public async Task AlternativeModifiersUseTheSameCLocaleConversions()
    {
        using var state = CreateState(new FakeTimeProvider());
        await state.DoStringAsync(
            """
            for _,set in ipairs({{'E','cCxXyY'},{'O','deHImMSuUVwWy'}}) do
                for conversion in set[2]:gmatch('.') do
                    assert(os.date('!%'..set[1]..conversion)==os.date('!%'..conversion))
                end
            end
            assert(not pcall(os.date,'!%Ea'))
            assert(not pcall(os.date,'!%Oc'))
            assert(not pcall(os.date,'!%OY'))
            """
        );
    }
}
