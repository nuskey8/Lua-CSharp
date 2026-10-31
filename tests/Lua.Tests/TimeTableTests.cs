using Lua.Platforms;
using Lua.Standard;
using Microsoft.Extensions.Time.Testing;

namespace Lua.Tests;

public class TimeTableTests
{
    static LuaState CreateState()
    {
        var provider = new FakeTimeProvider();
        provider.SetLocalTimeZone(TimeZoneInfo.Utc);
        var state = LuaState.Create(LuaPlatform.Default with { TimeProvider = provider });
        state.OpenStandardLibraries();
        return state;
    }

    [TestCase("year=2000,month=3,day=0", "2000-02-29 12:00:00")]
    [TestCase("year=2000,month=13,day=1", "2001-01-01 12:00:00")]
    [TestCase("year=2000,month=-12,day=1", "1998-12-01 12:00:00")]
    [TestCase("year=2000,month=2,day=30,hour=24,min=-1,sec=61", "2000-03-02 00:00:01")]
    [TestCase("year='2000.9',month='1.9',day=2.9,hour=3.9,min=4.9,sec=5.9", "2000-01-02 03:04:05")]
    [TestCase("year=2000,month=1,day=2,hour=-0.9,min=-1.9,sec=-1.9", "2000-01-01 23:58:59")]
    [TestCase("year=2000,month=1,day=2,hour=false,min={},sec='invalid'", "2000-01-02 12:00:00")]
    public async Task FieldsAreConvertedAndNormalizedWithoutUpdatingTheTable(
        string fields,
        string expected
    )
    {
        using var state = CreateState();
        var result = await state.DoStringAsync(
            "local t={"
                + fields
                + "}; local copy={}; for k,v in pairs(t) do copy[k]=v end; "
                + "local timestamp=os.time(t); for k,v in pairs(t) do assert(copy[k]==v) end; "
                + "for k,v in pairs(copy) do assert(t[k]==v) end; return os.date('!%Y-%m-%d %H:%M:%S',timestamp)"
        );
        Assert.That(result, Is.EqualTo(new LuaValue[] { expected }));
    }

    [TestCase("day")]
    [TestCase("month")]
    [TestCase("year")]
    public async Task InvalidRequiredFieldsRaiseLuaErrors(string field)
    {
        using var state = CreateState();
        state.Environment["field"] = field;
        await state.DoStringAsync(
            """
            local t={year=2000,month=1,day=1}
            for _,v in ipairs({false,{},'invalid'}) do
                t[field]=v
                local ok,err=pcall(os.time,t)
                assert(not ok and err:find("field '"..field.."' missing in date table",1,true))
            end
            t[field]=nil
            assert(not pcall(os.time,t))
            """
        );
    }

    [Test]
    public async Task FieldsUseIndexMetamethodsInLua52Order()
    {
        using var state = CreateState();
        await state.DoStringAsync(
            """
            local fields={sec=5,min=4,hour=3,day=2,month=1,year=2000,isdst=false}
            local order={}
            local t=setmetatable({}, {__index=function(_,key)
                order[#order+1]=key
                return fields[key]
            end})
            assert(os.date('!%Y-%m-%d %H:%M:%S',os.time(t))=='2000-01-02 03:04:05')
            assert(table.concat(order,',')=='sec,min,hour,day,month,year,isdst')
            assert(next(t)==nil)
            assert(os.time(setmetatable({}, {__index=fields}))==os.time(fields))
            local marker={}
            local ok,err=pcall(os.time,setmetatable({}, {__index=function() error(marker) end}))
            assert(not ok and err==marker)
            local co=coroutine.create(function()
                local ok=pcall(os.time,setmetatable({}, {__index=coroutine.yield}))
                assert(not ok)
                coroutine.yield('restored')
            end)
            local ok,value=coroutine.resume(co)
            assert(ok and value=='restored')
            """
        );
    }

    [Test]
    public async Task HostIndexCallbacksCanAwait()
    {
        using var state = CreateState();
        state.Environment["read"] = new LuaFunction(
            "read",
            async (context, _) =>
            {
                var key = context.GetArgument<string>(1);
                await Task.Yield();
                return context.Return(
                    key switch
                    {
                        "year" => 2000,
                        "month" => 1,
                        "day" => 2,
                        _ => LuaValue.Nil,
                    }
                );
            }
        );
        await state.DoStringAsync(
            """
            local t=setmetatable({}, {__index=read})
            assert(os.time(t)==946814400)
            """
        );
    }

    [Test]
    public async Task UnrepresentableDatesReturnNil()
    {
        using var state = CreateState();
        await state.DoStringAsync(
            """
            assert(os.time{year=10000,month=1,day=1}==nil)
            assert(os.time{year=2000,month=1,day=2147483647}==nil)
            assert(os.time{year=1969,month=12,day=31,hour=23,min=59,sec=59}==nil)
            """
        );
    }
}
