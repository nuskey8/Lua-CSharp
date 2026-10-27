using Lua;
using Lua.Platforms;
using Lua.Runtime;
using Lua.Standard;

public sealed class Workload : IDisposable
{
    public static readonly Dictionary<string, string> Sources = new()
    {
        ["numeric-for"] = "local s=0; for i=1,20000 do s=s+i end; return s",
        ["numeric-while"] = "local s,i=0,0; while i<10000 do s=s+i; i=i+1 end; return s",
        ["concat-pair"] =
            "local a,b='abc','def'; local n=0; for i=1,3000 do n=n+#(a..b) end; return n",
        ["concat-many"] =
            "local a,b,c,d='abc','def','ghi','jkl'; local n=0; for i=1,2000 do n=n+#(a..b..c..d..a..b..c..d) end; return n",
        ["concat-empty"] =
            "local a,b='','abc'; local n=0; for i=1,3000 do n=n+#(a..b..a..a) end; return n",
        ["concat-mixed"] =
            "local a,b='abc',42; local n=0; for i=1,2000 do n=n+#(a..b..a..b) end; return n",
        ["concat-meta"] =
            "local mt={__concat=function(a,b) return a.x..b.x end}; local a,b=setmetatable({x='a'},mt),setmetatable({x='b'},mt); local n=0; for i=1,1000 do n=n+#(a..b) end; return n",
        ["api-add-sync"] = "local f=host_api; local n=0; for i=1,2000 do n=n+f() end; return n",
        ["api-add-async"] = "local f=host_api; local n=0; for i=1,100 do n=n+f() end; return n",
        ["numeric-eq"] =
            "local s=0; for i=1,10000 do if i==0 then s=s-1 else s=s+i end end; return s",
        ["table-eq"] =
            "local a,b={},{}; local s=0; for i=1,3000 do if a==b then s=s-1 else s=s+i end end; return s",
        ["generic-for"] =
            "local t={}; for i=1,2000 do t[i]=i end; local s=0; for _,v in ipairs(t) do s=s+v end; return s",
        ["comparison-meta"] =
            "local mt={__lt=function(a,b) return a.x<b.x end}; local a=setmetatable({x=1},mt); local b=setmetatable({x=2},mt); local s=0; for i=1,2000 do if a<b then s=s+i end end; return s",
        ["vararg-meta"] =
            "local mt={__lt=function(a,...) local b=...; return a.x<b.x end}; local a=setmetatable({x=1},mt); local b=setmetatable({x=2},mt); local s=0; for i=1,2000 do if a<b then s=s+i end end; return s",
        ["arithmetic"] = "local x=1; for i=1,4000 do x=((x+i)*1.25-i/3)%997 end; return x",
        ["arithmetic-strings"] =
            "local a,b='7','3'; local s=0; for i=1,4000 do s=s+(a+b)*(a-b)/(a%b) end; return s",
        ["table-array"] =
            "local t={}; for i=1,2000 do t[i]=i end; local s=0; for i=1,2000 do s=s+t[i] end; return s",
        ["table-fields"] =
            "local t={x=1,y=2}; for i=1,5000 do t.x=t.x+t.y; t.y=t.y+1 end; return t.x",
        ["lua-calls"] =
            "local function add(a,b) return a+b end; local s=0; for i=1,2000 do s=add(s,i) end; return s",
        ["tail-calls"] =
            "local function sum(n,s) if n==0 then return s end; return sum(n-1,s+n) end; return sum(2000,0)",
        ["varargs"] =
            "local function f(a,...) local b,c=...; return a+b+c end; local s=0; for i=1,1500 do s=s+f(i,2,3) end; return s",
        ["math"] =
            "local abs,sqrt,min,max=math.abs,math.sqrt,math.min,math.max; local s=0; for i=1,2000 do s=s+min(abs(-i),sqrt(i))+max(i,3,7) end; return s",
        ["math-modf"] =
            "local modf=math.modf; local n=0; for i=1,3000 do local a,b=modf((i-1500)/3); n=n+a+b end; return n",
        ["select"] =
            "local select=select; local n=0; for i=1,3000 do local a,b=select(2,1,2,3); n=n+a+b+select('#',1,2,3) end; return n",
        ["tonumber-base"] =
            "local tonumber=tonumber; local n=0; for i=1,1000 do n=n+tonumber('123456',10)+tonumber('12ab',16)+tonumber('101010',2)+tonumber('z1',36) end; return n",
        ["tonumber"] =
            "local tonumber=tonumber; local n=0; for i=1,2000 do n=n+tonumber('12.5')+tonumber(i) end; return n",
        ["bit32-normalize"] =
            "local band,bor,bxor,bnot=bit32.band,bit32.bor,bit32.bxor,bit32.bnot; local n=0; for i=1,2000 do n=n+band(-i,255)+bor(i,256)+bxor(i,511)+bnot(i) end; return n",
        ["bit32-shifts"] =
            "local l,r,a,lr,rr=bit32.lshift,bit32.rshift,bit32.arshift,bit32.lrotate,bit32.rrotate; local n=0; for i=1,2000 do local d=i%63-31; n=n+l(i,d)+r(-i,d)+a(-i,d)+lr(i,d)+rr(i,d) end; return n",
        ["strings"] =
            "local rep,reverse,sub,byte=string.rep,string.reverse,string.sub,string.byte; local n=0; for i=1,1000 do local s=rep('abcd',8,':'); s=reverse(s); n=n+byte(sub(s,2,-2)) end; return n",
        ["string-rep"] =
            "local rep=string.rep; local n=0; for i=1,300 do n=n+#rep('abcdefgh',128,':') end; return n",
        ["string-sub"] =
            "local sub=string.sub; local s='abcdefghijklmnopqrstuvwxyz'; local n=0; for i=1,3000 do n=n+#sub(s,1,-1) end; return n",
        ["format-number-quote"] =
            "local format=string.format; local n=0; for i=1,1000 do n=n+#format('%q',i+0.125) end; return n",
        ["format-hex"] =
            "local format=string.format; local n=0; for i=1,1000 do n=n+#format('%a',i+0.125) end; return n",
        ["format-quote"] =
            """local format=string.format; local s=string.rep([[ordinary text with " and \ ]],4); local n=0; for i=1,500 do n=n+#format('%q',s) end; return n""",
        ["table-concat"] =
            "local concat=table.concat; local t={'abc',12,'def',34,'ghi',56}; local n=0; for i=1,1000 do n=n+#concat(t,',') end; return n",
        ["table-sort"] = "local t={}; for i=1,300 do t[i]=301-i end; table.sort(t); return t[150]",
        ["table-sort-custom"] =
            "local t={}; for i=1,300 do t[i]=301-i end; table.sort(t,function(a,b) return a<b end); return t[150]",
        ["table-unpack"] =
            "local unpack=table.unpack; local t={1,2,3,4,5,6,7,8}; local n=0; for i=1,3000 do local a,b,c,d,e,f,g,h=unpack(t); n=n+a+b+c+d+e+f+g+h end; return n",
        ["table-unpack-empty-meta"] =
            "local unpack=table.unpack; local t=setmetatable({1,2,3,4,5,6,7,8},{}); local n=0; for i=1,3000 do local a,b,c,d,e,f,g,h=unpack(t); n=n+a+b+c+d+e+f+g+h end; return n",
        ["table-moves"] =
            "local insert,remove=table.insert,table.remove; local t={1,2,3,4,5,6,7,8}; local n=0; for i=1,2000 do insert(t,2,i); n=n+remove(t,2) end; return n",
        ["patterns"] =
            "local gsub=string.gsub; local n=0; for i=1,400 do local s=gsub('hello 123 world 456','(%a+)', '[%1]'); n=n+#s end; return n",
        ["pattern-miss"] =
            "local find=string.find; local s=string.rep('ab',128); local n=0; for i=1,100 do if find(s,'(z+)%1')==nil then n=n+1 end end; return n",
        ["gsub-plain"] =
            "local gsub=string.gsub; local n=0; for i=1,500 do local s=gsub('abca abc abca','abc','x'); n=n+#s end; return n",
        ["gsub-number"] =
            "local gsub=string.gsub; local f=function() return 1.5 end; local n=0; for i=1,300 do local s=gsub('aba aba','a',f); n=n+#s end; return n",
        ["nbody"] = LoadNBody(),
        ["metamethods"] =
            "local mt={__add=function(a,b) return a.x+b.x end}; local a=setmetatable({x=3},mt); local b=setmetatable({x=4},mt); local n=0; for i=1,2000 do n=n+(a+b) end; return n",
        ["hooks"] =
            "local n=0; debug.sethook(function() n=n+1 end,'',100); local s=0; for i=1,2000 do s=s+i end; debug.sethook(); return s+n",
        ["coroutines"] =
            "local co=coroutine.create(function() for i=1,500 do coroutine.yield(i) end end); local s=0; for i=1,500 do local ok,x=coroutine.resume(co); assert(ok); s=s+x end; return s",
        ["compile-keywords"] =
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
        ["compile-identifiers"] =
            "local total=0;"
            + string.Concat(
                Enumerable
                    .Range(0, 80)
                    .Select(i =>
                        $"do local returnValue{i},functionName{i},ordinaryIdentifier{i}=1,2,3; total=total+returnValue{i}+functionName{i}+ordinaryIdentifier{i} end;"
                    )
            )
            + "return total",
        ["compile-plain-strings"] =
            "local total=0;"
            + string.Concat(
                Enumerable
                    .Range(0, 80)
                    .Select(i =>
                        $"do local value='ordinary string value {i}: the quick brown fox jumps over the lazy dog'; total=total+#value end;"
                    )
            )
            + "return total",
        ["compile-escaped-strings"] =
            "local total=0;"
            + string.Concat(
                Enumerable
                    .Range(0, 80)
                    .Select(i =>
                        $"do local value='escaped value {i}:\\tcolumn\\nline\\r\\a\\b\\f\\v\\\\\\\"\\\'\\x41\\065\\z   suffix'; total=total+#value end;"
                    )
            )
            + "return total",
    };

    static string LoadNBody()
    {
        using var source = typeof(Workload).Assembly.GetManifestResourceStream("n-body.lua")!;
        using var reader = new StreamReader(source);
        return reader.ReadToEnd().Replace("or 1000", "or 100") + "\nreturn energy(bodies, nbody)";
    }

    public LuaState State { get; }
    public LuaClosure Closure { get; }
    readonly LuaValue[] results = new LuaValue[1];
    readonly bool compile;
    readonly string name;

    public Workload(string name, bool bytes = false, bool compile = false)
    {
        this.name = name;
        this.compile = compile;
        State = LuaState.Create(bytes ? LuaPlatform.DefaultByteStrings : LuaPlatform.Default);
        State.OpenStandardLibraries();
        if (name is "api-add-sync" or "api-add-async")
            AddApiOperation(State, name == "api-add-async");
        Closure = State.Load(Sources[name], name);
    }

    static void AddApiOperation(LuaState state, bool suspend)
    {
        var operand = new LuaTable { Metatable = new LuaTable() };
        operand.Metatable["__add"] = new LuaFunction(
            async (context, token) =>
            {
                if (suspend)
                    await Task.Yield();
                return context.Return(42);
            }
        );
        state.Environment["host_api"] = new LuaFunction(
            async (context, token) =>
            {
                var result = await state.AddAsync(operand, operand, token);
                return context.Return(result);
            }
        );
    }

    public double Run()
    {
        if (compile)
            return State.Load(Sources[name], name).Proto.Code.Length;
        var task = State.ExecuteAsync(Closure, results);
        if (!task.IsCompletedSuccessfully)
            task.AsTask().GetAwaiter().GetResult();
        else
            task.GetAwaiter().GetResult();
        return results[0].Read<double>();
    }

    public void Dispose() => State.Dispose();
}
