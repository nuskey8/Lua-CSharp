using Lua.Runtime;
using Lua.Standard;

namespace Lua.Tests;

[LuaObject]
public partial class BindingBaseObject
{
    [LuaMember]
    public int Value { get; set; }

    [LuaMember("read")]
    public virtual int Read() => Value;
}

[LuaObject(BindInstanceMethods = true)]
public partial class BindingObject : BindingBaseObject
{
    public CancellationToken ObservedToken { get; private set; }

    public override int Read() => Value * 2;

    [LuaMember("add")]
    public int Add(int amount = 1) => Value += amount;

    [LuaMember("readOther")]
    public int ReadOther(BindingObject other) => other.Value;

    [LuaMember]
    public static int StaticValue() => 7;

    [LuaMember]
    public int CountAll(ReadOnlySpan<LuaValue> values) => values.Length;

    [LuaMember]
    public int Count(int prefix = 10, ReadOnlySpan<LuaValue> values = default) =>
        prefix + values.Length;

    [LuaMember]
    public int RefOut(out int doubled, ref int first, ReadOnlySpan<LuaValue> values)
    {
        doubled = first * 2;
        first++;
        return values.Length;
    }

    [LuaMember]
    public int Required(int value) => value;

    [LuaMember]
    public async ValueTask<LuaTable> Capture(
        int first,
        ReadOnlyMemory<LuaValue> values,
        CancellationToken ct
    )
    {
        ObservedToken = ct;
        await Task.Yield();
        ct.ThrowIfCancellationRequested();
        var result = new LuaTable { ["first"] = first, ["count"] = values.Length };
        for (var i = 0; i < values.Length; i++)
            result[i + 1] = values.Span[i];
        return result;
    }

    [LuaMember("length"), LuaMetamethod(LuaObjectMetamethod.Len)]
    public int Length() => Value;

    [LuaMember("multiply"), LuaMetamethod(LuaObjectMetamethod.Mul)]
    public int Multiply(BindingObject other) => Value * other.Value;
}

[LuaObject(BindInstanceMethods = true)]
public partial record BindingRecord(int Value)
{
    [LuaMember]
    public int Read() => Value;
}

public class LuaObjectBindingTests
{
    [Test]
    public async Task DotCallsRetainTheirReceiverAndFunctionIdentity()
    {
        using var state = LuaState.Create();
        state.OpenStandardLibraries();
        var first = new BindingObject();
        var second = new BindingObject { Value = 10 };
        state.Environment["first"] = first;
        state.Environment["second"] = second;

        var results = await state.DoStringAsync(
            """
            assert(first.add == first.add)
            assert(first.add ~= second.add)
            assert(first.StaticValue == second.StaticValue)
            local saved = first.add
            local function tail() return saved(3) end
            return first.add(2), tail(), first.readOther(first), first.readOther(second),
                   first.read(), saved()
            """
        );

        Assert.That(results, Is.EqualTo(new LuaValue[] { 2, 5, 5, 10, 10, 6 }));
        Assert.That(first.Value, Is.EqualTo(6));
        Assert.That(second.Value, Is.EqualTo(10));
    }

    [TestCase("CountAll()", 0)]
    [TestCase("CountAll(1, nil, 3)", 3)]
    [TestCase("Count()", 10)]
    [TestCase("Count(nil)", 10)]
    [TestCase("Count(7, nil, 3)", 9)]
    public async Task DotCallsPreserveVariadicAndOptionalArguments(string call, int expected)
    {
        using var state = LuaState.Create();
        state.Environment["target"] = new BindingObject();
        var results = await state.DoStringAsync($"return target.{call}");
        Assert.That(results, Is.EqualTo(new LuaValue[] { expected }));
    }

    [Test]
    public async Task DotCallsPreserveRefOutArgumentPositions()
    {
        using var state = LuaState.Create();
        state.Environment["target"] = new BindingObject();
        var results = await state.DoStringAsync("return target.RefOut(5, 100, 200)");
        Assert.That(results, Is.EqualTo(new LuaValue[] { 2, 10, 6 }));
    }

    [TestCase("Required()")]
    [TestCase("Required('invalid')")]
    public void InvalidArgumentsReportTheirLuaPosition(string call)
    {
        using var state = LuaState.Create();
        state.Environment["target"] = new BindingObject();
        var exception = Assert.ThrowsAsync<LuaRuntimeException>(async () =>
            await state.DoStringAsync($"return target.{call}")
        );
        Assert.That(exception!.Message, Does.Contain("bad argument #1"));
    }

    [Test]
    public async Task AsyncDotCallsPreserveArgumentsAndCancellation([Values] bool hooks)
    {
        using var state = LuaState.Create();
        state.OpenStandardLibraries();
        var target = new BindingObject();
        state.Environment["target"] = target;
        using var cancellation = new CancellationTokenSource();
        if (hooks)
            await state.DoStringAsync("debug.sethook(function() end, 'cr')");

        var results = await state.DoStringAsync(
            "local values = target.Capture(7, nil, 3, nil); return values.first, values.count, values[1], values[2], values[3]",
            cancellationToken: cancellation.Token
        );

        Assert.That(results, Is.EqualTo(new LuaValue[] { 7, 3, LuaValue.Nil, 3, LuaValue.Nil }));
        Assert.That(target.ObservedToken, Is.EqualTo(cancellation.Token));
    }

    [Test]
    public async Task MetamethodsKeepExplicitReceivers()
    {
        using var state = LuaState.Create();
        state.Environment["first"] = new BindingObject { Value = 3 };
        state.Environment["second"] = new BindingObject { Value = 7 };
        var results = await state.DoStringAsync(
            "return first.length(), #first, first.multiply(second), first * second"
        );
        Assert.That(results, Is.EqualTo(new LuaValue[] { 3, 3, 21, 21 }));
    }

    [Test]
    public async Task DefaultBindingsKeepExplicitReceivers()
    {
        using var state = LuaState.Create();
        state.Environment["first"] = new BindingBaseObject { Value = 7 };
        state.Environment["second"] = new BindingBaseObject { Value = 9 };
        var results = await state.DoStringAsync("return first:read(), first.read(second)");
        Assert.That(results, Is.EqualTo(new LuaValue[] { 7, 9 }));
    }

    [Test]
    public async Task RecordCachesPreserveEqualityAndCloneReceivers()
    {
        using var state = LuaState.Create();
        var first = new BindingRecord(7);
        var equal = new BindingRecord(7);
        state.Environment["first"] = first;
        var saved = (await state.DoStringAsync("return first.Read"))[0].Read<LuaFunction>();
        state.Environment["equal"] = equal;
        state.Environment["clone"] = first with { Value = 9 };

        var results = await state.DoStringAsync(
            "return first.Read(), equal.Read(), clone.Read(), first.Read, equal.Read"
        );

        Assert.That(results.Take(3), Is.EqualTo(new LuaValue[] { 7, 7, 9 }));
        Assert.That(results[3].Read<LuaFunction>(), Is.SameAs(saved));
        Assert.That(results[4].Read<LuaFunction>(), Is.Not.SameAs(saved));
        Assert.That(first, Is.EqualTo(equal));
        Assert.That(first.GetHashCode(), Is.EqualTo(equal.GetHashCode()));
    }

    [Test]
    public void ConcurrentLookupsPublishTheSameBoundMethod()
    {
        var target = new BindingObject();
        var functions = new LuaFunction[32];
        Parallel.For(
            0,
            functions.Length,
            i =>
            {
                using var state = LuaState.Create();
                state.Environment["target"] = target;
                functions[i] = state
                    .DoStringAsync("return target.add")
                    .GetAwaiter()
                    .GetResult()[0]
                    .Read<LuaFunction>();
            }
        );
        Assert.That(functions.All(x => ReferenceEquals(x, functions[0])), Is.True);
    }
}
