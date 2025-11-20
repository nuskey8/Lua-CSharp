using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using Lua.Runtime;
using Lua;
using Lua.Standard;

var state = LuaState.Create();
state.OpenStandardLibraries();
state.Environment["escape"] = new LuaFunction("escape",
    (c, _) =>
    {
        var arg = c.HasArgument(0) ? c.GetArgument<string>(0) : "";
        return new(c.Return(Regex.Escape(arg)));
    });
var source = "";
try
{
    source = File.ReadAllText(GetAbsolutePath("test.lua"));

    // Console.WriteLine("Source Code " + new string('-', 50));
    // Console.WriteLine(source);

    var closure = state.Load(source, "@test.lua");

    DebugChunk(closure.Proto, 0);

    Console.WriteLine("Output " + new string('-', 50));

    // Console.Read();

    var timer = new System.Diagnostics.Stopwatch();
    timer.Start();
    for (var i = 0; i < 1000; i++)
    {
        var count = await state.RunAsync(closure);
        state.Pop(count);
        if (i % 100 == 0)
        {
            Console.WriteLine($"Iteration {i} completed. Time elapsed: {timer.ElapsedMilliseconds} ms");
            Thread.Sleep(100);
        }
    }

    // Console.WriteLine("Result " + new string('-', 50));
    // using var results = state.RootAccess.ReadStack(count);
    // for (var i = 0; i < count; i++)
    // {
    //     Console.WriteLine(results[i]);
    // }

    Console.WriteLine("End " + new string('-', 50));
}
catch (Exception ex)
{
    if (ex is LuaCompileException luaCompileException)
    {
        Console.WriteLine("CompileError " + new string('-', 50));
        Console.WriteLine(RustLikeExceptionHook.OnCatch(source, luaCompileException));
        Console.WriteLine(new string('-', 55));
    }

    Console.WriteLine(ex);

    if (ex is LuaRuntimeException { InnerException: not null } luaEx)
    {
        Console.WriteLine("Inner Exception " + new string('-', 50));
        Console.WriteLine(luaEx.InnerException);
    }
}

static string GetAbsolutePath(string relativePath, [CallerFilePath] string callerFilePath = "")
{
    return Path.Combine(Path.GetDirectoryName(callerFilePath)!, relativePath);
}

static void DebugChunk(Prototype chunk, int id)
{
    Console.WriteLine($"Chunk[{id}]" + new string('=', 50));
    Console.WriteLine($"Parameters:{chunk.ParameterCount}");

    Console.WriteLine("Code " + new string('-', 50));
    var index = 0;
    foreach (var inst in chunk.Code)
    {
        Console.WriteLine($"[{index}]\t{chunk.LineInfo[index]}\t\t{inst}");
        index++;
    }

    Console.WriteLine("LocalVariables " + new string('-', 50));
    index = 0;
    foreach (var local in chunk.LocalVariables)
    {
        Console.WriteLine($"[{index}]\t{local.Name}\t{local.StartPc}\t{local.EndPc}");
        index++;
    }

    Console.WriteLine("Constants " + new string('-', 50));
    index = 0;
    foreach (var constant in chunk.Constants.ToArray())
    {
        Console.WriteLine($"[{index}]\t{Regex.Escape(constant.ToString())}");
        index++;
    }

    Console.WriteLine("UpValues " + new string('-', 50));
    index = 0;
    foreach (var upValue in chunk.UpValues.ToArray())
    {
        Console.WriteLine($"[{index}]\t{upValue.Name}\t{(upValue.IsLocal ? 1 : 0)}\t{upValue.Index}");
        index++;
    }

    Console.WriteLine();

    var nestedChunkId = 0;
    foreach (var localChunk in chunk.ChildPrototypes)
    {
        DebugChunk(localChunk, nestedChunkId);
        nestedChunkId++;
    }
}

public class LuaRustLikeException(string message, Exception? innerException) : Exception(message, innerException);

class RustLikeExceptionHook //: ILuaCompileHook
{
    public static string OnCatch(ReadOnlySpan<char> source, LuaCompileException exception)
    {
        var lineOffset = exception.OffSet - exception.Position.Column + 1;
        var length = 0;
        if (lineOffset < 0)
        {
            lineOffset = 0;
        }

        foreach (var c in source[lineOffset..])
        {
            if (c is '\n' or '\r')
            {
                break;
            }

            length++;
        }

        var builder = new StringBuilder();
        builder.AppendLine();
        builder.AppendLine("[error]: " + exception.MessageWithNearToken);
        builder.AppendLine("-->" + exception.ChunkName + ":" + exception.Position.Line + ":" + exception.Position.Column);
        var line = source.Slice(lineOffset, length).ToString();
        var lineNumString = exception.Position.Line.ToString();
        builder.AppendLine(new string(' ', lineNumString.Length) + " |");
        builder.AppendLine(lineNumString + " | " + line);
        builder.AppendLine(new string(' ', lineNumString.Length) + " | " +
                           new string(' ', exception.Position.Column - 1) +
                           "^ " + exception.MainMessage);
        return builder.ToString();
    }
}