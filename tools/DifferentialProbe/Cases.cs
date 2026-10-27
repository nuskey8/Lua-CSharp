using System.Globalization;

record Case(string Family, string Name, string Source);

static class Cases
{
    public static IEnumerable<Case> Generate()
    {
        string[] strings = ["''", "'a'", "'abc'", "'a b a'", "'a\\0b'", "'12ab34'"];
        string[] indices =
        [
            "-2147483649",
            "-8",
            "-4",
            "-1.9",
            "-0.5",
            "0",
            "0.5",
            "1",
            "1.9",
            "3",
            "4",
            "8",
            "2147483648",
            "'2'",
            "'-1.9'",
            "nil",
        ];
        foreach (var s in strings)
        foreach (var first in indices)
        {
            foreach (var last in indices)
            foreach (var method in new[] { "sub", "byte" })
                yield return new(
                    "string.slice",
                    $"{method}/{s}/{first}/{last}",
                    $"return string.{method}({s},{first},{last})"
                );

            foreach (var pattern in new[] { "''", "'a'", "'.'", "'()'", "'%z'" })
            foreach (var method in new[] { "find", "match" })
                yield return new(
                    "string.search",
                    $"{method}/{s}/{first}/{pattern}",
                    $"return string.{method}({s},{pattern},{first})"
                );
        }

        foreach (var c in Enumerable.Range(0, 33).Append(127))
        foreach (
            var suffix in new[]
            {
                "''",
                "'a'",
                "'0'",
                "'1'",
                "'2'",
                "'3'",
                "'4'",
                "'5'",
                "'6'",
                "'7'",
                "'8'",
                "'9'",
            }
        )
        {
            var setup = $"local s=string.char({c})..{suffix}; local q=string.format('%q',s); ";
            yield return new("string.quote", $"spelling/{c}/{suffix}", setup + "return q");
            yield return new(
                "string.quote-roundtrip",
                $"{c}/{suffix}",
                setup + "local f=assert(load('return '..q)); return f()==s"
            );
        }

        foreach (
            var n in new[]
            {
                "-1.9",
                "-0.5",
                "0",
                "0.5",
                "1.9",
                "65.9",
                "255.9",
                "256",
                "'65.9'",
                "nil",
                "false",
            }
        )
            yield return new("string.char", n, $"return string.char({n})");

        foreach (
            var value in new[]
            {
                "true",
                "false",
                "nil",
                "{}",
                "function() end",
                "coroutine.create(function() end)",
            }
        )
        {
            yield return new(
                "string.quote-types",
                $"first/{value}",
                $"return string.format('%q',{value})"
            );
            yield return new(
                "string.quote-types",
                $"later/{value}",
                $"return string.format('%s:%q','prefix',{value})"
            );
        }
        yield return new(
            "string.quote-types",
            "tostring-metamethod",
            "local calls=0; local x=setmetatable({}, {__tostring=function() calls=calls+1; return 'converted' end}); local ok=pcall(string.format,'%q',x); return ok,calls,string.format('%s',x),calls"
        );

        foreach (
            var value in new[]
            {
                "0",
                "-0.0",
                "12",
                "-12",
                "1.5",
                "1/3",
                "1e20",
                "1e-20",
                "2^53",
                "0x1p-1074",
                "-0x1p-1074",
                "0x1.fffffffffffffp1023",
                "1.234567890123456",
            }
        )
        foreach (var format in new[] { "%q", "%20q", "%+.2q" })
            yield return new(
                "string.quote-number",
                $"{format}/{value}",
                $"local x={value}; local q=string.format('{format}',x); return q,type(assert(load('return '..q))())"
            );

        foreach (
            var body in new[]
            {
                "return",
                "return nil",
                "return true",
                "return {}",
                "return 12",
                "return 'first','extra'",
            }
        )
        foreach (var hooks in new[] { false, true })
        {
            var setup = hooks ? "debug.sethook(function() end,'cr'); " : "";
            yield return new(
                "callback.results",
                $"tostring/{body}/{hooks}",
                setup
                    + $"local x=setmetatable({{}},{{__tostring=function() {body} end}}); local r=table.pack(tostring(x)); return r.n,type(r[1]),type(r[1])=='string' and r[1] or nil"
            );
        }
        yield return new(
            "callback.results",
            "sethook/enable",
            "return select('#',debug.sethook(function() end,'cr'))"
        );
        yield return new(
            "callback.results",
            "sethook/disable",
            "return select('#',debug.sethook(nil))"
        );
        yield return new(
            "callback.results",
            "upvaluejoin",
            "local a,b=1,2; local function f() return a end; local function g() return b end; local n=select('#',debug.upvaluejoin(f,1,g,1)); return n,f()"
        );

        string[] patterns =
        [
            "''",
            "'.'",
            "'.*'",
            "'.-'",
            "'a*'",
            "'a-'",
            "'a?'",
            "'a+'",
            "'^'",
            "'$'",
            "'^$'",
            "'()'",
            "'(a*)'",
            "'(.)%1'",
            "'((a)b)'",
            "'%b()'",
            "'%f[%a]%a+'",
            "'[%a%d]+'",
            "'[^a]*'",
            "'%z'",
            "'['",
            "'('",
            "')'",
            "'%'",
            "'%1'",
            "'()%1'",
            "'(a)%2'",
            "'a**'",
        ];
        foreach (var s in strings.Concat(["'(a)(b)'", "'^a^^b'", "'aaab abba'"]))
        foreach (var pattern in patterns)
        {
            foreach (var method in new[] { "find", "match" })
                yield return new(
                    "pattern.search",
                    $"{method}/{s}/{pattern}",
                    $"return string.{method}({s},{pattern})"
                );
            foreach (
                var repl in new[]
                {
                    "'x'",
                    "'%0'",
                    "'%1'",
                    "function(...) return select('#',...) end",
                }
            )
                yield return new(
                    "pattern.gsub",
                    $"{s}/{pattern}/{repl}",
                    $"return string.gsub({s},{pattern},{repl})"
                );
            yield return new(
                "pattern.gmatch",
                $"{s}/{pattern}",
                $"local t={{}}; for x in string.gmatch({s},{pattern}) do t[#t+1]=tostring(x) end; return table.concat(t,'|')"
            );
        }

        string[] numbers =
        [
            "'-0'",
            "' 2.5 '",
            "'0x1.8p2'",
            "'1e309'",
            "'nan'",
            "'NaN'",
            "'inf'",
            "'Infinity'",
            "'  +12'",
            "'12x'",
            "''",
            "'1_2'",
            "'0x'",
            "'0x1p'",
            "'1e'",
            "'2\\0'",
            "'\\1602'",
            "'2\\160'",
            "'0x2\\160'",
            "'\\v2\\f'",
        ];
        foreach (var n in numbers)
        {
            yield return new("number.parse", n, $"return tonumber({n})");
            yield return new("number.coercion", n, $"local x={n}; return x+0, -x");
            foreach (var b in new[] { "2", "8", "10", "16", "36", "16.9", "'16'" })
                yield return new("number.base", $"{n}/{b}", $"return tonumber({n},{b})");
        }

        foreach (
            var value in new[]
            {
                "1/3",
                "1e20",
                "-0.0",
                "0.000012345",
                "1e-20",
                "2^53",
                "1.234567890123456",
            }
        )
            yield return new(
                "number.text",
                value,
                $"local x={value}; return tostring(x),string.format('%s',x),x..''"
            );

        foreach (var value in new[] { "1/3", "1e14", "1e20", "0x1p-1074", "-0.0", "1.5" })
        {
            foreach (var replacement in new[] { "x", "function() return x end", "{a=x}" })
                yield return new(
                    "number.replacement",
                    $"{value}/{replacement}",
                    $"local x={value}; return string.gsub('aba','a',{replacement})"
                );
            foreach (var radix in new[] { 2, 8, 10, 16, 36 })
                yield return new(
                    "number.base-numeric",
                    $"{value}/{radix}",
                    $"return tonumber({value},{radix})"
                );
        }

        foreach (
            var method in new[]
            {
                "abs",
                "ceil",
                "floor",
                "sqrt",
                "exp",
                "log",
                "frexp",
                "modf",
                "deg",
                "rad",
                "sin",
                "atan",
            }
        )
        foreach (
            var n in new[]
            {
                "-1/0",
                "-1e300",
                "-2.5",
                "-0.5",
                "-0.0",
                "0",
                "0.5",
                "1",
                "2.5",
                "1e300",
                "1/0",
                "0/0",
                "'2.5'",
            }
        )
            yield return new("math", $"{method}/{n}", $"return math.{method}({n})");

        foreach (
            var n in new[]
            {
                "0x1p-1074",
                "-0x1p-1074",
                "0x0.fffffffffffffp-1022",
                "0x1p-1022",
                "0x1.fffffffffffffp1023",
                "-0x1.fffffffffffffp1023",
                "math.pi",
                "-math.pi",
                "0x1p-1021",
                "0x1p1000",
            }
        )
        foreach (var method in new[] { "deg", "rad", "frexp" })
            yield return new("math", $"boundary/{method}/{n}", $"return math.{method}({n})");
        foreach (
            var exponent in new[] { -1074, -1073, -1023, -1022, -1021, -53, -1, 0, 1, 52, 53, 1023 }
        )
        foreach (var sign in new[] { "", "-" })
            yield return new(
                "math",
                $"frexp/power/{sign}/{exponent}",
                $"return math.frexp({sign}0x1p{exponent})"
            );

        foreach (
            var specifier in new[] { "d", "i", "u", "x", "X", "o", "c", "f", "e", "E", "g", "G" }
        )
        foreach (var flags in new[] { "", "-", "+", " ", "0", "#", "+0", " 0", "-0", "++" })
        foreach (var precision in new[] { "", ".0", ".3" })
        foreach (
            var value in new[]
            {
                "-12.75",
                "-0.5",
                "-0.0",
                "0",
                "1.5",
                "12.75",
                "12345",
                "0.000012345",
                "'2.5'",
            }
        )
        {
            var format = "%" + flags + "8" + precision + specifier;
            var family =
                specifier == "c" ? "format.char"
                : specifier is "f" or "e" or "E" or "g" or "G" ? "format.float"
                : "format.integer";
            yield return new(
                family,
                $"{format}/{value}",
                $"return string.format('{format}',{value})"
            );
        }

        // Keep allocation-producing table ranges small and valid for native Lua.
        var tableIndices = indices.Where(i => i != "nil" && !i.Contains("214748364"));
        foreach (var position in tableIndices)
        {
            var setup = "local t={'a','b','c'}; ";
            yield return new(
                "table",
                $"insert/{position}",
                setup + $"table.insert(t,{position},'x'); return #t,table.concat(t,',')"
            );
            yield return new(
                "table",
                $"remove/{position}",
                setup + $"local x=table.remove(t,{position}); return x,#t,table.concat(t,',')"
            );
            foreach (var last in tableIndices)
            {
                yield return new(
                    "table",
                    $"unpack/{position}/{last}",
                    setup + $"return table.unpack(t,{position},{last})"
                );
                yield return new(
                    "table",
                    $"concat/{position}/{last}",
                    setup + $"return table.concat(t,',',{position},{last})"
                );
            }
        }

        foreach (
            var value in new[]
            {
                "1/3",
                "1e20",
                "-0.0",
                "0.000012345",
                "1e-20",
                "2^53",
                "1.234567890123456",
            }
        )
            yield return new(
                "table",
                $"concat-number/{value}",
                $"return table.concat({{{value},'x'}},',')"
            );

        foreach (var op in new[] { "+", "-", "*", "/", "%", "^", "<", "<=", "==", "~=" })
        foreach (var a in new[] { "-3", "-0.0", "0", "0.5", "2", "'2'", "'x'", "false", "nil" })
        foreach (var b in new[] { "-2", "0", "0.5", "3", "'3'", "'y'", "true", "nil" })
            yield return new(
                "vm.operators",
                $"{a}/{op}/{b}",
                $"local a,b={a},{b}; return a {op} b"
            );

        for (var n = 0; n < 20; n++)
        {
            var size = n.ToString(CultureInfo.InvariantCulture);
            yield return new(
                "compiler",
                $"closures/{n}",
                $"local t={{}}; for i=1,{size} do local x=i; t[i]=function(y) return x+y end end; local s=0; for i=1,#t do s=s+t[i](i) end; return s"
            );
            yield return new(
                "compiler",
                $"repeat/{n}",
                $"local x=0; repeat local y=x+1; x=y until y>={size}; return x"
            );
            yield return new(
                "compiler",
                $"varargs/{n}",
                $"local function f(...) return select('#',...),... end; local t={{}}; for i=1,{size} do t[i]=i end; return f(table.unpack(t))"
            );
            yield return new(
                "compiler",
                $"assignment/{n}",
                $"local t={{1,2,3}}; local i=1; i,t[i]=2,{size}; t[i],i=i,3; return i,table.unpack(t)"
            );
            yield return new(
                "compiler",
                $"table-conflict/{n}",
                $"local t={{}}; local old=t; t.x,t={size},{{}}; return old.x,t.x"
            );
            yield return new(
                "compiler",
                $"upvalue-conflict/{n}",
                $"local t={{}}; local old=t; local function f() t.x,t={size},{{}} end; f(); return old.x,t.x"
            );
            yield return new(
                "compiler",
                $"two-conflicts/{n}",
                $"local a,b={{}},{{}}; local i=1; a[i],b[i],i={size},{size}+1,2; return a[1],a[2],b[1],b[2],i"
            );
            yield return new(
                "compiler",
                $"rhs-call/{n}",
                $"local t={{}}; local i=1; local function f() return {size},2 end; t[i],i=f(); return t[1],t[2],i"
            );
        }

        foreach (
            var number in new[]
            {
                "0",
                "1",
                "1.",
                ".5",
                "1e2",
                "0x1",
                "0x1.8p2",
                "0x.8",
                "0x1p-2",
                "0x1+2",
                "0x1-2",
                "1..2",
                "1e",
                "0x",
                "0xp1",
                "0x1p",
                "0x1p+",
                "0x1g",
                "0x1.2.3",
            }
        )
            yield return new("scanner.number", number, "return " + number);

        for (var code = 0; code < 256; code++)
        {
            var escape = "\\" + code.ToString("D3", CultureInfo.InvariantCulture);
            yield return new(
                "scanner.escape",
                $"decimal/{code}",
                $"return string.byte('{escape}')"
            );
            escape = "\\x" + code.ToString("x2", CultureInfo.InvariantCulture);
            yield return new("scanner.escape", $"hex/{code}", $"return string.byte('{escape}')");
        }

        foreach (
            var word in new[]
            {
                "-4294967297",
                "-1.5",
                "-1",
                "-0.5",
                "0",
                "0.5",
                "1",
                "1.5",
                "255",
                "4294967295",
                "4294967296",
            }
        )
        {
            foreach (
                var count in new[]
                {
                    "-64",
                    "-33",
                    "-32",
                    "-31",
                    "-1.9",
                    "0",
                    "1.9",
                    "31",
                    "32",
                    "33",
                    "64",
                }
            )
            foreach (var method in new[] { "lshift", "rshift", "arshift", "lrotate", "rrotate" })
                yield return new(
                    "bit32",
                    $"{method}/{word}/{count}",
                    $"return bit32.{method}({word},{count})"
                );
            foreach (var second in new[] { "-1.5", "0", "0.5", "1", "255", "4294967295" })
            foreach (var method in new[] { "band", "bor", "bxor", "btest" })
                yield return new(
                    "bit32",
                    $"{method}/{word}/{second}",
                    $"return bit32.{method}({word},{second})"
                );
        }

        for (var n = 0; n < 30; n++)
        {
            yield return new(
                "vm.metamethod",
                $"index-chain/{n}",
                $"local a={{x={n}}}; local b=setmetatable({{}},{{__index=a}}); local c=setmetatable({{}},{{__index=b}}); return c.x,c.y"
            );
            yield return new(
                "vm.metamethod",
                $"newindex-chain/{n}",
                $"local a={{}}; local b=setmetatable({{}},{{__newindex=a}}); local c=setmetatable({{}},{{__newindex=b}}); c.x={n}; return a.x,rawget(b,'x'),rawget(c,'x')"
            );
            yield return new(
                "vm.metamethod",
                $"call/{n}",
                $"local x=setmetatable({{n={n}}},{{__call=function(self,...) return self.n,select('#',...),... end}}); return x(1,nil,3)"
            );
            yield return new(
                "vm.metamethod",
                $"le-fallback/{n}",
                $"local mt={{__lt=function(a,b) return a.x<b.x end}}; local a,b=setmetatable({{x={n}}},mt),setmetatable({{x=15}},mt); return a<b,a<=b,b<=a"
            );
            yield return new(
                "vm.coroutine",
                $"resume/{n}",
                $"local co=coroutine.create(function(x) local y=coroutine.yield(x,nil,x+1); return y,nil,y+1 end); local a,b,c,d=coroutine.resume(co,{n}); local e,f,g,h=coroutine.resume(co,{n}+2); return a,b,c,d,e,f,g,h,coroutine.status(co)"
            );
        }

        // Deterministic generated expression trees exercise folding, register
        // allocation and precedence without relying on transcendental rounding.
        var random = new Random(0x52);
        for (var n = 0; n < 1000; n++)
        {
            var expression = Expression(random, 3);
            yield return new(
                "compiler.expression",
                n.ToString(CultureInfo.InvariantCulture),
                "local x,y=3,7; return " + expression
            );
        }
    }

    static string Expression(Random random, int depth)
    {
        if (depth == 0)
            return new[] { "x", "y", "1", "2", "3", "7", "0" }[random.Next(7)];
        var left = Expression(random, depth - 1);
        var right = Expression(random, depth - 1);
        var op = new[] { "+", "-", "*", "/", "%" }[random.Next(5)];
        return random.Next(2) == 0 ? $"({left} {op} {right})" : $"{left} {op} {right}";
    }
}
