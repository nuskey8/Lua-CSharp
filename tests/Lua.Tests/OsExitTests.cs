using Lua.Platforms;
using Lua.Standard;

namespace Lua.Tests;

public class OsExitTests
{
    [TestCase("", 0)]
    [TestCase("nil", 0)]
    [TestCase("true", 0)]
    [TestCase("false", 1)]
    [TestCase("2.9", 2)]
    [TestCase("'-2.9'", -2)]
    [TestCase("'0x10'", 16)]
    public async Task ExitCoercesLuaCodesBeforeCallingTheHost(string argument, int expected)
    {
        var environment = new ExitEnvironment();
        using var state = LuaState.Create(LuaPlatform.Default with { OsEnvironment = environment });
        state.OpenStandardLibraries();
        await state.DoStringAsync($"pcall(os.exit{(argument.Length == 0 ? "" : "," + argument)})");
        Assert.That(environment.ExitCode, Is.EqualTo(expected));
    }

    [TestCase("{}")]
    [TestCase("'invalid'")]
    [TestCase("0/0")]
    [TestCase("math.huge")]
    public async Task InvalidCodesDoNotCallTheHost(string argument)
    {
        var environment = new ExitEnvironment();
        using var state = LuaState.Create(LuaPlatform.Default with { OsEnvironment = environment });
        state.OpenStandardLibraries();
        await state.DoStringAsync($"assert(not pcall(os.exit,{argument}))");
        Assert.That(environment.ExitCode, Is.Null);
    }

    sealed class ExitEnvironment : ILuaOsEnvironment
    {
        public int? ExitCode { get; private set; }

        public ValueTask Exit(int exitCode, CancellationToken cancellationToken)
        {
            ExitCode = exitCode;
            throw new InvalidOperationException("Exit requested");
        }

        public string? GetEnvironmentVariable(string name) => null;

        public double GetTotalProcessorTime() => 0;
    }
}
