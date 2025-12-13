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
}