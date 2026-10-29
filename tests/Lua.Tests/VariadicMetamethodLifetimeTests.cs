using System.Runtime.CompilerServices;
using Lua.Platforms;
using Lua.Standard;

namespace Lua.Tests;

[TestFixture(LuaStringMode.Utf16)]
[TestFixture(LuaStringMode.Bytes)]
public class VariadicMetamethodLifetimeTests(LuaStringMode mode)
{
    const int Iterations = 64;

    LuaState CreateState()
    {
        var state = LuaState.Create(
            mode == LuaStringMode.Bytes ? LuaPlatform.DefaultByteStrings : LuaPlatform.Default
        );
        state.OpenStandardLibraries();
        return state;
    }

    public static IEnumerable<TestCaseData> Operations()
    {
        (string Name, string Statement)[] operations =
        [
            ("__add", "n=n+(a+b)"),
            ("__sub", "n=n+(a-b)"),
            ("__mul", "n=n+(a*b)"),
            ("__div", "n=n+(a/b)"),
            ("__mod", "n=n+(a%b)"),
            ("__pow", "n=n+(a^b)"),
            ("__unm", "n=n+(-a)"),
            ("__len", "n=n+#a"),
            ("__eq", "if a==b then n=n+1 end"),
            ("__lt", "if a<b then n=n+1 end"),
            ("__le", "if a<=b then n=n+1 end"),
            ("reverse-le", "if a<=b then n=n+1 end"),
            ("__index", "n=n+a.missing"),
            ("global-index", "n=n+missing"),
            ("__newindex", "a.missing=i;n=n+1"),
            ("global-newindex", "missing=i;n=n+1"),
            ("self", "n=n+a:missing()"),
            ("__concat", "local value=a..b;if value=='joined' then n=n+1 end"),
            ("__call", "n=n+a(17,18)"),
        ];
        (int FixedParameters, bool Suspend, bool TailCall)[] variants =
        [
            (0, false, false),
            (1, false, false),
            (0, true, false),
            (1, true, false),
            (0, false, true),
            (0, true, true),
        ];
        foreach (var (name, statement) in operations)
        foreach (var (fixedParameters, suspend, tailCall) in variants)
        {
            // The arithmetic operators share argument preparation and result handling.
            if (
                name is "__sub" or "__mul" or "__div" or "__mod" or "__pow"
                && (fixedParameters != 0 || suspend || tailCall)
            )
                continue;
            yield return new TestCaseData(name, statement, fixedParameters, suspend, tailCall);
        }
    }

    [TestCaseSource(nameof(Operations))]
    public async Task CallsPreserveArgumentsAndUseConstantStackSpace(
        string operation,
        string statement,
        int fixedParameters,
        bool suspend,
        bool tailCall
    )
    {
        using var state = CreateState();
        var left = new LuaTable();
        var right = new LuaTable();
        state.Environment["left"] = left;
        state.Environment["right"] = right;
        var positions = new List<int>();
        var calls = 0;
        state.Environment["observe"] = new LuaFunction(
            async (context, _) =>
            {
                positions.Add(context.ReturnFrameBase);
                calls++;
                if (suspend)
                    await Task.Yield();

                var isGlobal = operation.StartsWith("global-");
                LuaValue first =
                    isGlobal ? state.Environment
                    : operation == "reverse-le" ? right
                    : left;
                LuaValue second = operation switch
                {
                    "__index" or "global-index" or "self" or "__newindex" or "global-newindex" =>
                        "missing",
                    "__unm" or "__len" or "reverse-le" => left,
                    "__call" => 17,
                    _ => right,
                };
                var isAssignment = operation is "__newindex" or "global-newindex";
                Assert.That(
                    context.ArgumentCount,
                    Is.EqualTo(isAssignment || operation == "__call" ? 3 : 2)
                );
                Assert.That(context.GetArgument(0), Is.EqualTo(first));
                Assert.That(context.GetArgument(1), Is.EqualTo(second));
                if (isAssignment || operation == "__call")
                    Assert.That(
                        context.GetArgument(2),
                        Is.EqualTo(new LuaValue(isAssignment ? calls : 18))
                    );
                return context.Return();
            }
        );
        var methodName = operation switch
        {
            "reverse-le" => "__lt",
            "global-index" or "self" => "__index",
            "global-newindex" => "__newindex",
            _ => operation,
        };
        var resultExpression = operation switch
        {
            "__eq" or "__lt" or "__le" => "true",
            "reverse-le" => "false",
            "self" => "function(self) assert(self==a);return 1 end",
            "__concat" => "'joined'",
            _ => "1",
        };
        var arguments = fixedParameters == 0 ? "..." : "head,...";
        var methodBody = tailCall
            ? $"return finish({arguments})"
            : $"observe({arguments});return {resultExpression},{arguments}";
        var result = await state.DoStringAsync(
            $$"""
            local a,b=left,right
            local observe=observe
            local function finish(head,...)
                observe(head,...)
                return {{resultExpression}},head,...
            end
            local function method({{arguments}}) {{methodBody}} end
            local mt={{{methodName}}=method}
            setmetatable(a,mt)
            setmetatable(b,mt)
            {{(operation.StartsWith("global-") ? "setmetatable(_ENV,mt)" : "")}}
            local n=0
            for i=1,{{Iterations}} do {{statement}} end
            return n
            """
        );
        Assert.That(result, Is.EqualTo(new LuaValue[] { Iterations }));
        Assert.That(calls, Is.EqualTo(Iterations));
        Assert.That(positions.Skip(1).Distinct().Count(), Is.EqualTo(1));
        Assert.That(state.Stack.Count, Is.Zero);
        Assert.That(state.CallStackFrameCount, Is.Zero);
    }

    [TestCase("__lt", "if item<a then n=n+1 end")]
    [TestCase("__add", "n=n+(item+a)")]
    [TestCase("__index", "n=n+item.missing")]
    [TestCase("__newindex", "a.missing=item;n=n+1")]
    [TestCase("__len", "n=n+#item")]
    public async Task DiscardedOperandsAreCollectibleDuringExecution(
        string methodName,
        string statement
    )
    {
        using var state = CreateState();
        var references = InstallOperands(state);
        var calls = 0;
        state.Environment["observe"] = new LuaFunction(
            (context, _) =>
            {
                if (++calls == Iterations)
                {
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                    GC.Collect();
                    Assert.That(
                        references.Take(Iterations - 1).Any(reference => reference.IsAlive),
                        Is.False
                    );
                }
                return new(context.Return());
            }
        );
        var result = await state.DoStringAsync(
            $$"""
            local observe=observe
            local mt={{{methodName}}=function(...)
                observe(...)
                return {{(methodName == "__lt" ? "true" : "1")}},...
            end}
            for i=1,{{Iterations}} do setmetatable(items[i],mt) end
            local a=setmetatable({},mt)
            local n=0
            for i=1,{{Iterations}} do
                local item=items[i]
                items[i]=nil
                {{statement}}
                item=nil
            end
            return n
            """
        );
        Assert.That(result, Is.EqualTo(new LuaValue[] { Iterations }));
        Assert.That(calls, Is.EqualTo(Iterations));
        Assert.That(state.Stack.Count, Is.Zero);
        Assert.That(state.CallStackFrameCount, Is.Zero);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static List<WeakReference> InstallOperands(LuaState state)
    {
        var references = new List<WeakReference>();
        var items = new LuaTable();
        for (var i = 1; i <= Iterations; i++)
        {
            var payload = new object();
            references.Add(new(payload));
            items[i] = new LuaTable { ["payload"] = LuaValue.FromObject(payload) };
        }
        state.Environment["items"] = items;
        return references;
    }
}
