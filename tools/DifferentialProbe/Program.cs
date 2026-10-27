using System.Globalization;
using System.Text.Json;
using Lua;
using Lua.Platforms;
using Lua.Standard;
using NuLua;
using NuLua.Lua52;

if (args.Length is < 1 or > 3)
{
    Console.Error.WriteLine(
        "Usage: DifferentialProbe <results.jsonl> [family-prefix] [bytes|utf16]"
    );
    return 2;
}

var mode = args.Length > 2 ? args[2] : "bytes";
if (mode is not ("bytes" or "utf16"))
{
    Console.Error.WriteLine("Expected bytes or utf16.");
    return 2;
}
var platform = mode == "bytes" ? LuaPlatform.DefaultByteStrings : LuaPlatform.Default;
var counts = new SortedDictionary<string, (int Total, int Different)>();
var outputPath = Path.GetFullPath(args[0]);
Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
using var output = new StreamWriter(outputPath);
var timer = System.Diagnostics.Stopwatch.StartNew();
foreach (
    var test in Cases
        .Generate()
        .Where(c => args.Length < 2 || c.Family.StartsWith(args[1], StringComparison.Ordinal))
)
{
    // Pcall tests report success/results or just failure. Diagnostics are retained
    // for top-level failures but not compared: engines have different tracebacks.
    var source =
        "local function run() "
        + test.Source
        + " end; "
        + "local r=table.pack(pcall(run)); if not r[1] then return false end; "
        +
        // Encode in Lua before crossing NuLua's UTF-8 string boundary so raw
        // bytes (including NUL and invalid UTF-8) compare without decoding loss.
        "for i=2,r.n do if type(r[i])=='string' then local bytes={}; "
        + "for j=1,#r[i] do bytes[j]=string.byte(r[i],j) end; "
        + "r[i]=table.concat(bytes,',') end end; return table.unpack(r,1,r.n)";
    Result managed;
    try
    {
        using var state = LuaState.Create(platform);
        state.OpenStandardLibraries();
        managed = new(
            (await state.DoStringAsync(source))
                .Select(v =>
                    v.Type
                    + ":"
                    + (v.Type == Lua.LuaValueType.Number ? Number(v.Read<double>()) : v.ToString())
                )
                .ToArray()
        );
    }
    catch (Exception e)
    {
        managed = new(null, e.GetType().Name, e.Message);
    }

    Result native;
    try
    {
        using var state = Lua52State.Create();
        state.OpenLibraries();
        native = new(
            state
                .DoString(source)
                .Select(v =>
                    v.Type
                    + ":"
                    + (v.Type.ToString() == "Number" ? Number(v.Read<double>()) : v.ToString())
                )
                .ToArray()
        );
    }
    catch (Exception e)
    {
        native = new(null, e.GetType().Name, e.Message);
    }

    // Unexpected host exceptions must never be counted as equivalent Lua errors.
    var equal =
        managed.Values is not null && native.Values is not null
            ? managed.Values.SequenceEqual(native.Values)
            : managed.Error is "LuaParseException" or "LuaCompileException" or "LuaRuntimeException"
                && native.Error == "LuaException";
    await output.WriteLineAsync(
        JsonSerializer.Serialize(
            new
            {
                test.Family,
                test.Name,
                test.Source,
                Mode = mode,
                Equal = equal,
                LuaCSharp = managed,
                NuLua52 = native,
            }
        )
    );
    var count = counts.GetValueOrDefault(test.Family);
    counts[test.Family] = (count.Total + 1, count.Different + (equal ? 0 : 1));
}

foreach (var (family, count) in counts)
    Console.WriteLine($"{family, -24} {count.Total, 5} cases; {count.Different, 4} differences");
if (counts.Count == 0)
{
    Console.Error.WriteLine("No cases matched the family prefix.");
    return 2;
}
Console.WriteLine(
    $"Total: {counts.Values.Sum(c => c.Total)} cases, {counts.Values.Sum(c => c.Different)} differences; {timer.Elapsed.TotalSeconds:F1}s; {mode}"
);
return counts.Values.Any(c => c.Different != 0) ? 1 : 0;

static string Number(double value) => value.ToString("R", CultureInfo.InvariantCulture);

record Result(string[]? Values, string? Error = null, string? Message = null);
