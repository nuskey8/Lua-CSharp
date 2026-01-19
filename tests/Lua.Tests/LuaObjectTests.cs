using Lua.Standard;

namespace Lua.Tests;

[LuaObject]
public partial class LuaTestObj
{
    int x;
    int y;

    [LuaMember("x")]
    public int X
    {
        get => x;
        set => x = value;
    }

    [LuaMember("y")]
    public int Y
    {
        get => y;
        set => y = value;
    }

    [LuaMember("create")]
    public static LuaTestObj Create(int x, int y)
    {
        return new LuaTestObj() { x = x, y = y };
    }

    [LuaMetamethod(LuaObjectMetamethod.Add)]
    public static LuaTestObj Add(LuaTestObj a, LuaTestObj b)
    {
        return new LuaTestObj() { x = a.x + b.x, y = a.y + b.y };
    }

    [LuaMetamethod(LuaObjectMetamethod.Sub)]
    public static async Task<LuaTestObj> Sub(LuaTestObj a, LuaTestObj b)
    {
        await Task.Delay(1);
        return new LuaTestObj() { x = a.x - b.x, y = a.y - b.y };
    }

    [LuaMetamethod(LuaObjectMetamethod.Len)]
    public async Task<double> Len()
    {
        await Task.Delay(1);
        return x + y;
    }
    
    [LuaMetamethod(LuaObjectMetamethod.Unm)]
    public LuaTestObj Unm()
    {
        return new LuaTestObj() { x = -x, y = -y };
    }

    [LuaMember]
    public object GetObj() => this;
}

[LuaObject]
public partial class TestUserData
{
    [LuaMember] public int Property { get; init; }

    [LuaMember] public int ReadOnlyProperty { get; }

    [LuaMember] public int SetOnlyProperty { set { } }

    [LuaMember] public LuaValue LuaValueProperty { get; set; }

    [LuaMember("p2")] public string PropertyWithName { get; set; } = "";

    [LuaMember]
    public static void MethodVoid()
    {
        Console.WriteLine("HEY!");
    }

    [LuaMember]
    public static async Task MethodAsync()
    {
        await Task.CompletedTask;
    }

    [LuaMember]
    public static double StaticMethodWithReturnValue(double a, double b)
    {
        Console.WriteLine($"HEY! {a} {b}");
        return a + b;
    }

    [LuaMember]
    public double InstanceMethodWithReturnValue()
    {
        return Property;
    }

    [LuaMember]
    public async ValueTask<LuaValue> InstanceMethodWithReturnValueAsync(LuaValue value, CancellationToken ct)
    {
        await Task.Delay(1, ct);
        return value;
    }

    [LuaMetamethod(LuaObjectMetamethod.Call)]
    public string Call()
    {
        return "Called!";
    }
}

[LuaObject]
public partial class IntArrayUserData
{
    public int[] Array { get; } = new int[10];

    public int this[int index]
    {
        get => Array[index];
        set => Array[index] = value;
    }

    [LuaMetamethod(LuaObjectMetamethod.Index)]
    public int GetAt(int index)
    {
        return Array[index];
    }

    [LuaMetamethod(LuaObjectMetamethod.NewIndex)]
    public void SetAt(int index, int value)
    {
        Array[index] = value;
    }
}

public class LuaObjectTests
{
    [Test]
    public async Task Test_Property()
    {
        var userData = new TestUserData { Property = 1 };

        var state = LuaState.Create();
        state.Environment["test"] = userData;
        var results = await state.DoStringAsync("return test.Property");

        Assert.That(results, Has.Length.EqualTo(1));
        Assert.That(results[0], Is.EqualTo(new LuaValue(1)));
    }

    [Test]
    public async Task Test_PropertyWithName()
    {
        var userData = new TestUserData { PropertyWithName = "foo" };

        var state = LuaState.Create();
        state.Environment["test"] = userData;
        var results = await state.DoStringAsync("return test.p2");

        Assert.That(results, Has.Length.EqualTo(1));
        Assert.That(results[0], Is.EqualTo(new LuaValue("foo")));
    }

    [Test]
    public async Task Test_MethodVoid()
    {
        var userData = new TestUserData();

        var state = LuaState.Create();
        state.Environment["test"] = userData;
        var results = await state.DoStringAsync("return test.MethodVoid()");

        Assert.That(results, Has.Length.EqualTo(0));
    }

    [Test]
    public async Task Test_MethodAsync()
    {
        var userData = new TestUserData();

        var state = LuaState.Create();
        state.Environment["test"] = userData;
        var results = await state.DoStringAsync("return test.MethodAsync()");

        Assert.That(results, Has.Length.EqualTo(0));
    }

    [Test]
    public async Task Test_StaticMethodWithReturnValue()
    {
        var userData = new TestUserData();

        var state = LuaState.Create();
        state.Environment["test"] = userData;
        var results = await state.DoStringAsync("return test.StaticMethodWithReturnValue(1, 2)");

        Assert.That(results, Has.Length.EqualTo(1));
        Assert.That(results[0], Is.EqualTo(new LuaValue(3)));
    }

    [Test]
    public async Task Test_InstanceMethodWithReturnValue()
    {
        var userData = new TestUserData { Property = 1 };

        var state = LuaState.Create();
        state.Environment["test"] = userData;
        var results = await state.DoStringAsync("return test:InstanceMethodWithReturnValue()");

        Assert.That(results, Has.Length.EqualTo(1));
        Assert.That(results[0], Is.EqualTo(new LuaValue(1)));
    }

    [Test]
    public async Task Test_InstanceMethodWithReturnValueAsync()
    {
        var userData = new TestUserData { Property = 1 };

        var state = LuaState.Create();
        state.Environment["test"] = userData;
        var results = await state.DoStringAsync("return test:InstanceMethodWithReturnValueAsync(2)");

        Assert.That(results, Has.Length.EqualTo(1));
        Assert.That(results[0], Is.EqualTo(new LuaValue(2)));
    }

    [Test]
    public async Task Test_CallMetamethod()
    {
        var userData = new TestUserData();

        var state = LuaState.Create();
        state.OpenBasicLibrary();
        state.Environment["test"] = userData;
        var results = await state.DoStringAsync("""
                                                assert(test() == 'Called!')
                                                return test()
                                                """);

        Assert.That(results, Has.Length.EqualTo(1));
        Assert.That(results[0], Is.EqualTo(new LuaValue("Called!")));
    }

    [Test]
    public async Task Test_ArithMetamethod()
    {
        var userData = new LuaTestObj();

        var state = LuaState.Create();
        state.OpenBasicLibrary();
        state.Environment["TestObj"] = userData;
        var results = await state.DoStringAsync("""
                                                local a = TestObj.create(1, 2)
                                                local b = TestObj.create(3, 4)
                                                return a + b, a - b
                                                """);
        Assert.That(results, Has.Length.EqualTo(2));
        Assert.That(results[0].Read<object>(), Is.TypeOf<LuaTestObj>());
        var objAdd = results[0].Read<LuaTestObj>();
        Assert.That(objAdd.X, Is.EqualTo(4));
        Assert.That(objAdd.Y, Is.EqualTo(6));
        Assert.That(results[1].Read<object>(), Is.TypeOf<LuaTestObj>());
        var objSub = results[1].Read<LuaTestObj>();
        Assert.That(objSub.X, Is.EqualTo(-2));
        Assert.That(objSub.Y, Is.EqualTo(-2));
    }
    [Test]
    public async Task Test_IndexMetamethod()
    {
        var userData = new IntArrayUserData();

        userData[0] = 1;
        userData[1] = 1;
        userData[2] = 2;
        userData[3] = 3;
        userData[4] = 5;

        var state = LuaState.Create();
        state.OpenBasicLibrary();
        state.Environment["TestObj"] = userData;
        var results = await state.DoStringAsync("""
                                                return TestObj[0], TestObj[1], TestObj[2], TestObj[3], TestObj[4]
                                                """);
        Assert.That(results, Has.Length.EqualTo(5));
        Assert.That(results[0].TryRead<int>(out var result), Is.True);
        Assert.That(result, Is.EqualTo(1));
        Assert.That(results[1].TryRead<int>(out result), Is.True);
        Assert.That(result, Is.EqualTo(1));
        Assert.That(results[2].TryRead<int>(out result), Is.True);
        Assert.That(result, Is.EqualTo(2));
        Assert.That(results[3].TryRead<int>(out result), Is.True);
        Assert.That(result, Is.EqualTo(3));
        Assert.That(results[4].TryRead<int>(out result), Is.True);
        Assert.That(result, Is.EqualTo(5));
    }
    
    [Test]
    public async Task Test_LenMetamethod()
    {
        var userData = new LuaTestObj();

        var state = LuaState.Create();
        state.OpenBasicLibrary();
        state.Environment["TestObj"] = userData;
        var results = await state.DoStringAsync("""
                                                function testLen(obj)
                                                    return #obj
                                                end
                                                local obj = TestObj.create(1, 2)
                                                return testLen(TestObj.create(1, 2)),-obj
                                                """);
        Assert.That(results, Has.Length.EqualTo(2));
        Assert.That(results[0].Read<double>(), Is.EqualTo(3));
        Assert.That(results[1].Read<object>(), Is.TypeOf<LuaTestObj>());
        var objUnm = results[1].Read<LuaTestObj>();
        Assert.That(objUnm.X, Is.EqualTo(-1));
        Assert.That(objUnm.Y, Is.EqualTo(-2));
        
    }
}