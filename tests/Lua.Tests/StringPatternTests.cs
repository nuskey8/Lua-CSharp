using System.Globalization;
using Lua.Platforms;
using Lua.Standard;

namespace Lua.Tests;

[TestFixture(LuaStringMode.Utf16)]
[TestFixture(LuaStringMode.Bytes)]
public class StringPatternTests(LuaStringMode mode)
{
    LuaState CreateState()
    {
        var state = LuaState.Create(
            mode == LuaStringMode.Bytes ? LuaPlatform.DefaultByteStrings : LuaPlatform.Default
        );
        state.OpenStandardLibraries();
        return state;
    }

    [TestCase("abc", "^.", "")]
    [TestCase("^a ^b", "^.", "^a,^b")]
    [TestCase("abc", "^", "")]
    [TestCase("^a^^b", "^", "^,^,^")]
    public async Task GmatchDoesNotInterpretCaretAsAnchor(
        string source,
        string pattern,
        string expected
    )
    {
        using var state = CreateState();
        state.Environment["source"] = source;
        state.Environment["pattern"] = pattern;
        var values = await state.DoStringAsync(
            "local t={}; for x in string.gmatch(source,pattern) do t[#t+1]=x end; return table.concat(t,',')"
        );
        Assert.That(values[0].Read<string>(), Is.EqualTo(expected));
    }

    [Test]
    public async Task GmatchExhaustionHasNoResultsIncludingAfterAnEmptyMatch()
    {
        using var state = CreateState();
        await state.DoStringAsync(
            """
            local f=string.gmatch('x','x')
            assert(f()=='x' and select('#',f())==0 and select('#',f())==0)
            f=string.gmatch('','')
            assert(f()=='' and select('#',f())==0)
            f=string.gmatch('abc','z')
            assert(select('#',f())==0)
            """
        );
    }

    [TestCase("string.match('abc','(')")]
    [TestCase("string.find('abc','(')")]
    [TestCase("string.gmatch('abc','(')()")]
    [TestCase("string.gsub('abc','(',{})")]
    [TestCase("string.gsub('a','(a)(',function(...) return 'x' end)")]
    public async Task UnfinishedCapturesRaiseLuaErrorsAndCleanTheStack(string operation)
    {
        using var state = CreateState();
        var values = await state.DoStringAsync(
            "return pcall(function() return " + operation + " end)"
        );
        Assert.That(values[0].Read<bool>(), Is.False);
        Assert.That(values[1].Read<string>(), Does.Contain("unfinished capture"));
        Assert.That(state.Stack.Count, Is.Zero);
        Assert.That(state.CallStackFrameCount, Is.Zero);
    }

    [TestCase("false", "'x'", "0")]
    [TestCase("true", "'x'", "1")]
    [TestCase("nil", "'a'", "0")]
    [TestCase("false", "'a'", "-1")]
    public async Task ReplacementTypeIsCheckedEvenWithoutAMatch(
        string replacement,
        string pattern,
        string limit
    )
    {
        using var state = CreateState();
        var values = await state.DoStringAsync(
            $"return pcall(string.gsub,'abc',{pattern},{replacement},{limit})"
        );
        Assert.That(values[0].Read<bool>(), Is.False);
        Assert.That(values[1].Read<string>(), Does.Contain("bad argument #3"));
    }

    [TestCase("1.5", "xaa", 1)]
    [TestCase("2.5", "xxa", 2)]
    [TestCase("0.5", "aaa", 0)]
    [TestCase("-0.5", "aaa", 0)]
    [TestCase("-1", "xxx", 3)]
    [TestCase("-1.5", "xxx", 3)]
    public async Task GsubLimitsFollowLua52IntegerAndUnsignedConversion(
        string limit,
        string expected,
        int count
    )
    {
        using var state = CreateState();
        var values = await state.DoStringAsync($"return string.gsub('aaa','a','x',{limit})");
        Assert.That(values[0].Read<string>(), Is.EqualTo(expected));
        Assert.That(values[1].Read<double>(), Is.EqualTo(count));
    }

    [Test]
    public async Task ReusedCaptureStorageHandlesFailedAttemptsBackreferencesAndMaximumCaptures()
    {
        using var state = CreateState();
        await state.DoStringAsync(
            """
            local a,b=string.match('aaac aab','((a+)b)'); assert(a=='aab' and b=='aa')
            a,b=string.match('ac ab','(a)(b)'); assert(a=='a' and b=='b')
            assert(string.match('ac abab','((ab)%2)')=='abab')
            local pattern=string.rep('()',32)..'a'
            local t=table.pack(string.match('xa',pattern))
            assert(t.n==32); for i=1,32 do assert(t[i]==2) end
            local s,n=string.gsub('ac ab','(a)(b)','%2%1'); assert(s=='ac ba' and n==1)
            s,n=string.gsub('ab','a','%1%0'); assert(s=='aab' and n==1)
            s,n=string.gsub('abc','(','x'); assert(s=='xaxbxcx' and n==4)
            """
        );
    }

    [Test]
    public async Task NumericReplacementIsCultureIndependentAndCallbacksKeepTheirArguments()
    {
        using var state = CreateState();
        var originalCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            await state.DoStringAsync(
                """
                local s,n=string.gsub('aba','a',1.5); assert(s=='1.5b1.5' and n==2)
                s,n=string.gsub('aba','a',function(...) assert(select('#',...)==1 and (...)=='a'); return 1.5 end)
                assert(s=='1.5b1.5' and n==2)
                s,n=string.gsub('ab','()',function(pos) return pos end); assert(s=='1a2b3' and n==3)
                s,n=string.gsub('ab','(.)',{a=12.5,b=false}); assert(s=='12.5b' and n==2)
                """
            );
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }
    }

    [Test]
    public async Task SuspendedReplacementPreservesCapturesAndRestoresStack()
    {
        using var state = CreateState();
        var calls = 0;
        state.Environment["replace"] = new LuaFunction(
            async (context, token) =>
            {
                var a = context.GetArgument<string>(0);
                var b = context.GetArgument<string>(1);
                await Task.Yield();
                calls++;
                return context.Return(b + a);
            }
        );
        var values = await state.DoStringAsync("return string.gsub('ac ab ab','(a)(b)',replace)");
        Assert.That(values[0].Read<string>(), Is.EqualTo("ac ba ba"));
        Assert.That(values[1].Read<double>(), Is.EqualTo(2));
        Assert.That(calls, Is.EqualTo(2));
        Assert.That(state.Stack.Count, Is.Zero);
        Assert.That(state.CallStackFrameCount, Is.Zero);
    }
}
