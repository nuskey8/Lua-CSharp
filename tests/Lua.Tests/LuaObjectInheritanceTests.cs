using Lua.Standard;

namespace Lua.Tests;

[LuaObject]
public partial class InheritanceBaseObject
{
    [LuaMember("value")]
    public virtual int Value { get; set; }

    [LuaMember("field")]
    public int Field;

    [LuaMember("read")]
    public virtual string Read() => "base";

    [LuaMember("inherited")]
    public int Inherited() => 7;

    [LuaMember("dual"), LuaMetamethod(LuaObjectMetamethod.Len)]
    public int Dual() => 1;

    [LuaMetamethod(LuaObjectMetamethod.Call)]
    public static InheritanceBaseObject New() => new();

    [LuaMetamethod(LuaObjectMetamethod.ToString)]
    public override string ToString() => "base";

    [LuaMetamethod(LuaObjectMetamethod.Index)]
    public string Index(string key) => "base:" + key;

    [LuaMetamethod(LuaObjectMetamethod.NewIndex)]
    public void NewIndex(string key, int value) => Field = value;
}

[LuaObject]
public partial class InheritanceDerivedObject : InheritanceBaseObject
{
    [LuaMember("value")]
    public override int Value { get; set; }

    [LuaMember("field")]
    public new int Field;

    [LuaMember("read")]
    public override string Read() => "derived";

    [LuaMember("dual")]
    public int MemberOnly() => 2;

    [LuaMetamethod(LuaObjectMetamethod.Call)]
    public static new InheritanceDerivedObject New() => new();

    [LuaMetamethod(LuaObjectMetamethod.ToString)]
    public override string ToString() => "derived";

    [LuaMetamethod(LuaObjectMetamethod.Index)]
    public string DerivedIndex(string key) => "derived:" + key;

    [LuaMetamethod(LuaObjectMetamethod.NewIndex)]
    public void DerivedNewIndex(string key, int value) => Field = value * 10;
}

[LuaObject]
public partial class InheritanceLeafObject : InheritanceDerivedObject
{
    [LuaMetamethod(LuaObjectMetamethod.Len)]
    public int Length() => 3;
}

[LuaObject]
public partial class InheritanceMetamethodOnlyObject : InheritanceBaseObject
{
    [LuaMetamethod(LuaObjectMetamethod.Len)]
    public int Length() => 4;
}

[LuaObject]
public partial class InheritanceUnannotatedOverrideObject : InheritanceBaseObject
{
    public override string Read() => "unannotated";

    public override string ToString() => "unannotated";

    public static new InheritanceBaseObject New() => new InheritanceUnannotatedOverrideObject();
}

[LuaObject]
public partial class InheritanceAliasObject : InheritanceBaseObject
{
    [LuaMember("value")]
    public int DifferentValue { get; set; }

    [LuaMember("read")]
    public int DifferentRead() => 42;

    [LuaMember("inherited")]
    public int MethodReplacedByProperty { get; set; }

    [LuaMember("field")]
    public int PropertyReplacedByMethod() => 99;
}

public class LuaObjectInheritanceTests
{
    [Test]
    public async Task DerivedBindings_AreUsedWithoutChangingBaseBindings()
    {
        using var state = LuaState.Create();
        state.OpenStandardLibraries();
        var parent = new InheritanceBaseObject();
        var child = new InheritanceDerivedObject();
        state.Environment["parent"] = parent;
        state.Environment["child"] = child;

        var results = await state.DoStringAsync(
            """
            parent.value = 10
            parent.field = 11
            child.value = 20
            child.field = 21
            child.unknown = 3
            return parent:read(), child:read(), parent.value, child.value,
                   parent.field, child.field, parent:inherited(), child:inherited(),
                   tostring(parent), tostring(child), parent.missing, child.missing,
                   parent(), child()
            """
        );

        Assert.That(
            results.Take(12),
            Is.EqualTo(
                new LuaValue[]
                {
                    "base",
                    "derived",
                    10,
                    20,
                    11,
                    30,
                    7,
                    7,
                    "base",
                    "derived",
                    "base:missing",
                    "derived:missing",
                }
            )
        );
        Assert.That(results[12].Read<object>(), Is.TypeOf<InheritanceBaseObject>());
        Assert.That(results[13].Read<object>(), Is.TypeOf<InheritanceDerivedObject>());
        Assert.That(((InheritanceBaseObject)child).Field, Is.Zero);
    }

    [Test]
    public async Task LeafObjects_KeepIntermediateOverridesAndIndependentBindings()
    {
        using var state = LuaState.Create();
        state.OpenStandardLibraries();
        state.Environment["parent"] = new InheritanceBaseObject();
        state.Environment["child"] = new InheritanceDerivedObject();
        state.Environment["leaf"] = new InheritanceLeafObject();
        state.Environment["metaOnly"] = new InheritanceMetamethodOnlyObject();

        var results = await state.DoStringAsync(
            """
            leaf.value = 12
            leaf.field = 13
            return parent:dual(), #parent, child:dual(), #child, leaf:dual(), #leaf,
                   leaf:read(), leaf.value, leaf.field, tostring(leaf), leaf.missing,
                   metaOnly:dual(), #metaOnly, leaf()
            """
        );

        Assert.That(
            results.Take(13),
            Is.EqualTo(
                new LuaValue[]
                {
                    1,
                    1,
                    2,
                    1,
                    2,
                    3,
                    "derived",
                    12,
                    13,
                    "derived",
                    "derived:missing",
                    1,
                    4,
                }
            )
        );
        Assert.That(results[13].Read<object>(), Is.TypeOf<InheritanceDerivedObject>());
    }

    [Test]
    public async Task UnannotatedOverrides_KeepInheritedExposure()
    {
        using var state = LuaState.Create();
        state.OpenStandardLibraries();
        state.Environment["obj"] = new InheritanceUnannotatedOverrideObject();

        var results = await state.DoStringAsync("return obj:read(), tostring(obj), obj()");

        Assert.That(results[0], Is.EqualTo(new LuaValue("unannotated")));
        Assert.That(results[1], Is.EqualTo(new LuaValue("unannotated")));
        Assert.That(results[2].Read<object>(), Is.TypeOf<InheritanceUnannotatedOverrideObject>());
    }

    [Test]
    public async Task LuaNames_SelectDerivedMembersAcrossMemberKinds()
    {
        using var state = LuaState.Create();
        var obj = new InheritanceAliasObject();
        state.Environment["obj"] = obj;

        var results = await state.DoStringAsync(
            """
            obj.value = 12
            obj.inherited = 13
            return obj.value, obj:read(), obj.inherited, obj:field()
            """
        );

        Assert.That(results, Is.EqualTo(new LuaValue[] { 12, 42, 13, 99 }));
        Assert.That(obj.DifferentValue, Is.EqualTo(12));
        Assert.That(obj.MethodReplacedByProperty, Is.EqualTo(13));
        Assert.That(obj.Value, Is.Zero);
    }
}
