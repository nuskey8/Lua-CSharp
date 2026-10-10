using Lua.Standard;

namespace Lua.Tests;

[LuaObject]
public partial class VariadicUserData
{
    public CancellationToken ObservedToken { get; private set; }

    [LuaMember]
    public static int StaticSpan(ReadOnlySpan<LuaValue> values) => values.Length;

    [LuaMember]
    public int InstanceSpan(ReadOnlySpan<LuaValue> values) => values.Length;

    [LuaMember]
    public static int StaticMemory(ReadOnlyMemory<LuaValue> values) => values.Length;

    [LuaMember]
    public int InstanceMemory(ReadOnlyMemory<LuaValue> values) => values.Length;

    [LuaMember]
    public static LuaTable StaticPrefix(
        int first,
        ReadOnlySpan<LuaValue> values,
        CancellationToken ct
    ) => Capture(first, values);

    [LuaMember]
    public LuaTable InstancePrefix(
        int first,
        ReadOnlyMemory<LuaValue> values,
        CancellationToken ct
    ) => Capture(first, values.Span);

    [LuaMember]
    public int OptionalPrefix(int first = 10, ReadOnlySpan<LuaValue> values = default) =>
        first + values.Length;

    [LuaMember]
    public int RefOut(
        out int extra,
        ref int first,
        ReadOnlySpan<LuaValue> values,
        CancellationToken ct
    )
    {
        extra = ++first;
        return values.Length;
    }

    [LuaMember]
    public static bool TokenOnly(CancellationToken ct) => ct.CanBeCanceled;

    [LuaMember]
    public bool SpanToken(ReadOnlySpan<LuaValue> values, CancellationToken ct) =>
        ct.CanBeCanceled && values.Length == 2;

    [LuaMember]
    public static async Task<LuaTable> AsyncMemory(
        int first,
        ReadOnlyMemory<LuaValue> values,
        CancellationToken ct
    )
    {
        await Task.Yield();
        ct.ThrowIfCancellationRequested();
        return Capture(first, values.Span);
    }

    [LuaMember]
    public async ValueTask<LuaTable> AsyncInstanceMemory(ReadOnlyMemory<LuaValue> values)
    {
        await Task.Yield();
        return Capture(0, values.Span);
    }

    [LuaMember]
    public async Task<int> AsyncToken(ReadOnlyMemory<LuaValue> values, CancellationToken ct)
    {
        ObservedToken = ct;
        await Task.Yield();
        ct.ThrowIfCancellationRequested();
        return values.Length;
    }

    static LuaTable Capture(int first, ReadOnlySpan<LuaValue> values)
    {
        var result = new LuaTable { ["first"] = first, ["count"] = values.Length };
        for (var i = 0; i < values.Length; i++)
            result[i + 1] = values[i];
        return result;
    }
}

public abstract partial class VariadicBase
{
    [LuaMember]
    public virtual int Inherited(ReadOnlyMemory<LuaValue> values) => values.Length;
}

[LuaObject]
public partial class VariadicDerived : VariadicBase { }

[LuaObject]
public partial class VariadicMetamethodUserData
{
    public LuaValue LastKey { get; private set; }
    public LuaValue LastValue { get; private set; }

    [LuaMetamethod(LuaObjectMetamethod.Call)]
    public int Call(ReadOnlySpan<LuaValue> values) => values.Length;

    [LuaMetamethod(LuaObjectMetamethod.Index)]
    public LuaValue Index(out int extra, ReadOnlySpan<LuaValue> values)
    {
        extra = values.Length;
        return values[0];
    }

    [LuaMetamethod(LuaObjectMetamethod.NewIndex)]
    public void NewIndex(out int extra, ReadOnlyMemory<LuaValue> values)
    {
        extra = values.Length;
        LastKey = values.Span[0];
        LastValue = values.Span[1];
    }
}

public class LuaObjectVariadicTests
{
    [TestCase("target.StaticSpan", "")]
    [TestCase("target:InstanceSpan", "")]
    [TestCase("target.StaticMemory", "")]
    [TestCase("target:InstanceMemory", "")]
    [TestCase("target.StaticSpan", "1, nil, 'three'")]
    [TestCase("target:InstanceSpan", "1, nil, 'three'")]
    [TestCase("target.StaticMemory", "1, nil, 'three'")]
    [TestCase("target:InstanceMemory", "1, nil, 'three'")]
    public async Task ArgumentViews_ExcludeReceiverAndPreserveCount(string method, string arguments)
    {
        using var state = LuaState.Create();
        state.Environment["target"] = new VariadicUserData();
        var result = await state.DoStringAsync($"return {method}({arguments})");
        Assert.That(result, Is.EqualTo(new LuaValue[] { arguments.Length == 0 ? 0 : 3 }));
    }

    [TestCase("target.StaticPrefix")]
    [TestCase("target:InstancePrefix")]
    [TestCase("target.AsyncMemory")]
    public async Task FixedArguments_AreExcludedAndNilIsPreserved(string method)
    {
        using var state = LuaState.Create();
        state.Environment["target"] = new VariadicUserData();
        var result = await state.DoStringAsync(
            $"local t = {method}(7, nil, 'last', nil) return t.first, t.count, t[1], t[2], t[3]"
        );
        Assert.That(
            result,
            Is.EqualTo(new LuaValue[] { 7, 3, LuaValue.Nil, "last", LuaValue.Nil })
        );
    }

    [TestCase("", 10)]
    [TestCase("nil", 10)]
    [TestCase("7", 7)]
    [TestCase("7, nil, 'last'", 9)]
    public async Task MissingOptionalPrefix_LeavesAnEmptyView(string arguments, int expected)
    {
        using var state = LuaState.Create();
        state.Environment["target"] = new VariadicUserData();
        var result = await state.DoStringAsync($"return target:OptionalPrefix({arguments})");
        Assert.That(result, Is.EqualTo(new LuaValue[] { expected }));
    }

    [Test]
    public async Task OutParameters_DoNotConsumeVariadicValues()
    {
        using var state = LuaState.Create();
        state.Environment["target"] = new VariadicUserData();
        var result = await state.DoStringAsync("return target:RefOut(5, 100, 200)");
        Assert.That(result, Is.EqualTo(new LuaValue[] { 2, 6, 6 }));
    }

    [Test]
    public async Task CancellationToken_IsInjectedWithoutConsumingLuaArguments()
    {
        using var state = LuaState.Create();
        state.Environment["target"] = new VariadicUserData();
        using var source = new CancellationTokenSource();
        var result = await state.DoStringAsync(
            "return target.TokenOnly(), target:SpanToken(1, 2)",
            cancellationToken: source.Token
        );
        Assert.That(result, Is.EqualTo(new LuaValue[] { true, true }));
    }

    [Test]
    public async Task AsyncMemory_ReceivesCancellationToken()
    {
        using var state = LuaState.Create();
        var target = new VariadicUserData();
        state.Environment["target"] = target;
        using var source = new CancellationTokenSource();
        var result = await state.DoStringAsync(
            "return target:AsyncToken(7, 1, 2)",
            cancellationToken: source.Token
        );
        Assert.That(result, Is.EqualTo(new LuaValue[] { 3 }));
        Assert.That(target.ObservedToken, Is.EqualTo(source.Token));
    }

    [Test]
    public async Task AsyncInstanceMemory_PreservesArgumentsAcrossAwait()
    {
        using var state = LuaState.Create();
        state.Environment["target"] = new VariadicUserData();
        var result = await state.DoStringAsync(
            "local t = target:AsyncInstanceMemory(1, nil, 3) return t.count, t[1], t[2], t[3]"
        );
        Assert.That(result, Is.EqualTo(new LuaValue[] { 3, 1, LuaValue.Nil, 3 }));
    }

    [Test]
    public void MissingRequiredPrefix_ReportsLuaArgumentError()
    {
        using var state = LuaState.Create();
        state.Environment["target"] = new VariadicUserData();
        var exception = Assert.ThrowsAsync<LuaRuntimeException>(async () =>
            await state.DoStringAsync("return target:InstancePrefix()")
        );
        Assert.That(exception!.Message, Does.Contain("bad argument #1"));
    }

    [Test]
    public async Task VariadicMetamethods_ReceiveKeyValueAndCallArguments()
    {
        using var state = LuaState.Create();
        var target = new VariadicMetamethodUserData();
        state.Environment["target"] = target;
        var result = await state.DoStringAsync(
            "target.answer = 42; return target(), target(1, nil, 3), target.unknown, target[7]"
        );
        Assert.That(result, Is.EqualTo(new LuaValue[] { 0, 3, "unknown", 7 }));
        Assert.That(target.LastKey, Is.EqualTo(new LuaValue("answer")));
        Assert.That(target.LastValue, Is.EqualTo(new LuaValue(42)));
    }

    [Test]
    public async Task InheritedVariadicMethod_IsBoundOnDerivedObject()
    {
        using var state = LuaState.Create();
        state.Environment["target"] = new VariadicDerived();
        var result = await state.DoStringAsync(
            "return target:Inherited(), target:Inherited(1, nil, 3)"
        );
        Assert.That(result, Is.EqualTo(new LuaValue[] { 0, 3 }));
    }
}
