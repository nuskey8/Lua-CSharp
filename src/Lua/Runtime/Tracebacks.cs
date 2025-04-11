using System.Globalization;
using System.Runtime.CompilerServices;
using Lua.CodeAnalysis;
using Lua.Internal;

namespace Lua.Runtime;

public class Traceback
{
    public required Closure RootFunc { get; init; }
    public required CallStackFrame[] StackFrames { get; init; }

    internal string RootChunkName => RootFunc.Proto.Name; //StackFrames.Length == 0 ? "" : StackFrames[^1].Function is Closure closure ? closure.Proto.GetRoot().Name : StackFrames[^2].Function.Name;

    internal SourcePosition LastPosition
    {
        get
        {
            var stackFrames = StackFrames.AsSpan();
            for (var index = stackFrames.Length - 1; index >= 0; index--)
            {
                LuaFunction lastFunc = index > 0 ? stackFrames[index - 1].Function : RootFunc;
                var frame = stackFrames[index];
                if (lastFunc is Closure closure)
                {
                    var p = closure.Proto;
                    if (frame.CallerInstructionIndex < p.SourcePositions.Length)
                        return p.SourcePositions[frame.CallerInstructionIndex];
                }
            }

            return default;
        }
    }


    public override string ToString()
    {
        using var list = new PooledList<char>(64);
        list.AddRange("stack traceback:\n");
        var stackFrames = StackFrames.AsSpan();
        var intFormatBuffer = (stackalloc char[15]);
        for (var index = stackFrames.Length - 1; index >= 0; index--)
        {
            LuaFunction lastFunc = index > 0 ? stackFrames[index - 1].Function : RootFunc;
            var frame = stackFrames[index];
            if (lastFunc is not null and not Closure)
            {
                list.AddRange("\t[C#]: in function '");
                list.AddRange(lastFunc.Name);
                list.AddRange("'\n");
            }
            else if (lastFunc is Closure closure)
            {
                var p = closure.Proto;
                var root = p.GetRoot();
                list.AddRange("\t");
                list.AddRange(root.Name);
                list.AddRange(":");
                p.SourcePositions[frame.CallerInstructionIndex].Line.TryFormat(intFormatBuffer, out var charsWritten,provider:CultureInfo.InvariantCulture);
                list.AddRange(intFormatBuffer[..charsWritten]);
                list.AddRange(root == p ? ": in '" : ": in function '");
                list.AddRange(p.Name);
                list.AddRange("'\n");
            }
        }

        return list.AsSpan().ToString();
    }
}
