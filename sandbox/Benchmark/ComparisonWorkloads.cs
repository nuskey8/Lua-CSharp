using System.Reflection;

public sealed record ComparisonCase(string Name, string Category, string Source, double Expected)
{
    public override string ToString() => Name;
}

public static class ComparisonWorkloads
{
    // ASCII input keeps the UTF-16, byte-string and native UTF-8 engines comparable.
    // Every invocation rebuilds its mutable Lua data and returns one numeric checksum.
    public static readonly ComparisonCase[] Cases =
    [
        new("numeric-loop", "VM", "local s=0; for i=1,20000 do s=s+i end; return s", 200010000),
        new(
            "calls-varargs",
            "VM",
            "local function f(a,...) local b,c=...; return a+b+c end; local s=0; for i=1,2000 do s=s+f(i,2,3) end; return s",
            2011000
        ),
        new(
            "table-access",
            "Tables",
            "local t={}; for i=1,1000 do t[i]={x=i,y=i%7} end; local s=0; for i=1,1000 do local v=t[i]; s=s+v.x+v.y end; return s",
            503503
        ),
        new(
            "metamethods",
            "VM",
            "local mt={__add=function(a,b) return a.x+b.x end,__index=function(t,k) return 5 end}; local a,b=setmetatable({x=3},mt),setmetatable({x=4},mt); local s=0; for i=1,2000 do s=s+(a+b)+a.missing end; return s",
            24000
        ),
        new(
            "math",
            "Standard",
            "local abs,sqrt,min,max=math.abs,math.sqrt,math.min,math.max; local s=0; for i=1,2000 do s=s+min(abs(-i),sqrt(i))+max(i,3,7) end; return s",
            MathExpected()
        ),
        new(
            "strings",
            "Standard",
            "local rep,reverse,sub,byte,concat=string.rep,string.reverse,string.sub,string.byte,table.concat; local n=0; local t={'abc',12,'def',34}; for i=1,400 do local s=sub(reverse(rep('abcd',8,':')),2,-2); n=n+byte(s)+#s+#concat(t,',') end; return n",
            59600
        ),
        new(
            "patterns",
            "Standard",
            "local gsub=string.gsub; local n=0; for i=1,400 do local s=gsub('hello 123 world 456','(%a+)', '[%1]'); n=n+#s end; return n",
            9200
        ),
        new(
            "table-sort",
            "Tables",
            "local t={}; for i=1,300 do t[i]=301-i end; table.sort(t); return t[150]",
            150
        ),
        new(
            "coroutines",
            "Coroutines",
            "local co=coroutine.create(function() for i=1,500 do coroutine.yield(i) end end); local s=0; for i=1,500 do local ok,x=coroutine.resume(co); assert(ok); s=s+x end; return s",
            125250
        ),
        new(
            "host-calls",
            "Interop",
            "local add=host_add; local s=0; for i=1,2000 do s=add(s,i) end; return s",
            2001000
        ),
        new("nbody", "Mixed", LoadNBody(), -0.1690507623824094),
    ];

    public static readonly ComparisonCase[] CompilationCases =
    [
        Find("numeric-loop"),
        Find("nbody"),
        new(
            "control-flow",
            "Compiler",
            "local total=0;"
                + string.Concat(
                    Enumerable.Repeat(
                        "do local function choose(value) if not value or value==false then return nil elseif value==true then return 1 else return value end end; "
                            + "local value=choose(2); repeat value=value-1 until value==0; while value<2 do value=value+1; if value==1 then goto skip end; total=total+value; ::skip:: end; "
                            + "for index=1,2 do if index==2 then break end; total=total+index end end;",
                        40
                    )
                )
                + "return total",
            120
        ),
        new(
            "identifiers",
            "Compiler",
            "local total=0;"
                + string.Concat(
                    Enumerable
                        .Range(0, 80)
                        .Select(i =>
                            $"do local returnValue{i},functionName{i},ordinaryIdentifier{i}=1,2,3; total=total+returnValue{i}+functionName{i}+ordinaryIdentifier{i} end;"
                        )
                )
                + "return total",
            480
        ),
        StringConstants(false),
        StringConstants(true),
    ];

    static ComparisonCase StringConstants(bool escaped)
    {
        var source = new System.Text.StringBuilder("local total=0;");
        var expected = 0;
        for (var i = 0; i < 80; i++)
        {
            var decoded = escaped
                ? $"escaped value {i}:\tcolumn\nline\r\a\b\f\v\\\"'AAsuffix"
                : $"ordinary string value {i}: the quick brown fox jumps over the lazy dog";
            var literal = escaped
                ? $"escaped value {i}:\\tcolumn\\nline\\r\\a\\b\\f\\v\\\\\\\"\\'\\x41\\065\\z   suffix"
                : decoded;
            source.Append("do local value='").Append(literal).Append("'; total=total+#value end;");
            expected += decoded.Length;
        }
        source.Append("return total");
        return new(
            escaped ? "escaped-strings" : "plain-strings",
            "Compiler",
            source.ToString(),
            expected
        );
    }

    public static ComparisonCase FindCompilation(string name) =>
        CompilationCases.Single(c => c.Name == name);

    static double MathExpected()
    {
        double sum = 0;
        for (var i = 1; i <= 2000; i++)
            sum += Math.Sqrt(i) + Math.Max(i, 7);
        return sum;
    }

    static string LoadNBody()
    {
        using var stream = Assembly
            .GetExecutingAssembly()
            .GetManifestResourceStream("Comparison.n-body.lua")!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd().Replace("or 1000", "or 100") + "\nreturn energy(bodies,nbody)";
    }

    public static ComparisonCase Find(string name) => Cases.Single(c => c.Name == name);

    public static void Check(ComparisonCase workload, string engine, double actual)
    {
        var tolerance = Math.Max(1, Math.Abs(workload.Expected)) * 1e-10;
        if (!double.IsFinite(actual) || Math.Abs(actual - workload.Expected) > tolerance)
            throw new InvalidOperationException(
                $"{engine}/{workload.Name}: expected {workload.Expected:R}, got {actual:R}"
            );
    }
}
