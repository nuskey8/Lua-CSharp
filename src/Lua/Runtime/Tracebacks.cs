using System.Globalization;
using Lua.Internal;

namespace Lua.Runtime;

public class Traceback(LuaState state, ReadOnlySpan<CallStackFrame> stackFrames)
{
    public LuaState State => state;
    public LuaFunction RootFunc => StackFrames[0].Function;
    readonly CallStackFrame[] stackFramesArray = stackFrames.ToArray();
    public ReadOnlySpan<CallStackFrame> StackFrames => stackFramesArray;

    internal static void WriteLastLuaTrace(ReadOnlySpan<CallStackFrame> stackFrames, ref PooledList<char> list, int level = 1)
    {
        if (level < 1) return;
        var intFormatBuffer = (stackalloc char[15]);
        var shortSourceBuffer = (stackalloc char[59]);
        for (var index = stackFrames.Length - level; index >= 1; index--)
        {
            LuaFunction lastFunc = stackFrames[index - 1].Function;
            var frame = stackFrames[index];
            if (!frame.IsTailCall && lastFunc is LuaClosure closure)
            {
                var p = closure.Proto;
                var len = LuaDebug.WriteShortSource(p.ChunkName, shortSourceBuffer);
                list.AddRange(shortSourceBuffer[..len]);
                list.AddRange(":");
                if (p.LineInfo.Length <= frame.CallerInstructionIndex)
                {
                    list.AddRange("Trace back error");
                }
                else
                {
                    p.LineInfo[frame.CallerInstructionIndex].TryFormat(intFormatBuffer, out var charsWritten, provider: CultureInfo.InvariantCulture);
                    list.AddRange(intFormatBuffer[..charsWritten]);
                }

                list.AddRange(": ");
                return;
            }
        }
    }

    internal void WriteLastLuaTrace(ref PooledList<char> list, int level = 1)
    {
        WriteLastLuaTrace(StackFrames, ref list, level);
    }

    public int LastLine
    {
        get
        {
            var stackFrames = StackFrames;
            for (var index = stackFrames.Length - 1; index >= 1; index--)
            {
                LuaFunction lastFunc = stackFrames[index - 1].Function;
                var frame = stackFrames[index];
                if (!frame.IsTailCall && lastFunc is LuaClosure closure)
                {
                    var p = closure.Proto;
                    if (frame.CallerInstructionIndex < 0 || p.LineInfo.Length <= frame.CallerInstructionIndex)
                    {
                        Console.WriteLine($"Trace back error");
                        return default;
                    }

                    return p.LineInfo[frame.CallerInstructionIndex];
                }
            }


            return default;
        }
    }

    public int FirstLine
    {
        get
        {
            var stackFrames = StackFrames;
            for (var index = 1; index <= stackFrames.Length; index++)
            {
                LuaFunction lastFunc = stackFrames[index - 1].Function;
                var frame = stackFrames[index];
                if (!frame.IsTailCall && lastFunc is LuaClosure closure)
                {
                    var p = closure.Proto;
                    if (frame.CallerInstructionIndex < 0 || p.LineInfo.Length <= frame.CallerInstructionIndex)
                    {
                        Console.WriteLine($"Trace back error");
                        return default;
                    }

                    return p.LineInfo[frame.CallerInstructionIndex];
                }
            }

            return default;
        }
    }

    public override string ToString()
    {
        return CreateTracebackMessage(State, StackFrames, LuaValue.Nil);
    }

    public string ToString(int skipFrames)
    {
        if (skipFrames < 0 || skipFrames >= StackFrames.Length)
        {
            return "stack traceback:\n";
        }

        return CreateTracebackMessage(State, StackFrames, LuaValue.Nil, skipFrames);
    }

    public static string CreateTracebackMessage(LuaThread thread, LuaValue message, int stackFramesSkipCount = 0)
    {
        return CreateTracebackMessage(thread.State, thread.GetCallStackFrames(), message, stackFramesSkipCount);
    }

    internal static string CreateTracebackMessage(LuaState state, ReadOnlySpan<CallStackFrame> stackFrames, LuaValue message, int skipCount = 0)
    {
        using var list = new PooledList<char>(64);
        if (message.Type is not LuaValueType.Nil)
        {
            list.AddRange(message.ToString());
            list.AddRange("\n");
        }

        list.AddRange("stack traceback:\n");
        var intFormatBuffer = (stackalloc char[15]);
        var shortSourceBuffer = (stackalloc char[59]);

        for (var index = stackFrames.Length - 1; index >= 0; index--)
        {
            LuaFunction lastFunc = stackFrames[index].Function;
            if (lastFunc is not null and not LuaClosure)
            {
                if (1 <= skipCount--)
                {
                    continue;
                }

                list.AddRange("\t[C#]: in function '");
                list.AddRange(lastFunc.Name);
                list.AddRange("'\n");
            }
            else if (lastFunc is LuaClosure closure)
            {
                if (index == stackFrames.Length - 1 || 1 <= skipCount--)
                {
                    continue;
                }

                var frame = stackFrames[index + 1];

                if (frame.IsTailCall)
                {
                    list.AddRange("\t(...tail calls...)\n");
                }

                var p = closure.Proto;
                list.AddRange("\t");
                var len = LuaDebug.WriteShortSource(p.ChunkName, shortSourceBuffer);
                list.AddRange(shortSourceBuffer[..len]);
                list.AddRange(":");
                if (p.LineInfo.Length <= frame.CallerInstructionIndex)
                {
                    list.AddRange("Trace back error");
                }
                else
                {
                    p.LineInfo[frame.CallerInstructionIndex].TryFormat(intFormatBuffer, out var charsWritten, provider: CultureInfo.InvariantCulture);
                    list.AddRange(intFormatBuffer[..charsWritten]);
                }


                list.AddRange(": in ");
                if (p.LineDefined == 0)
                {
                    list.AddRange("main chunk");
                    list.Add('\n');
                    goto Next;
                }

                if (stackFrames[index].Flags.HasFlag(CallStackFrameFlags.InHook))
                {
                    list.AddRange("hook");
                    list.AddRange(" '");
                    list.AddRange("?");
                    list.AddRange("'\n");
                    goto Next;
                }

                foreach (var pair in state.Environment.Dictionary)
                {
                    if (pair.Key.TryReadString(out var name)
                        && pair.Value.TryReadFunction(out var result) &&
                        result == closure)
                    {
                        list.AddRange("function '");
                        list.AddRange(name);
                        list.AddRange("'\n");
                        goto Next;
                    }
                }

                var caller = index > 0 ? stackFrames[index - 1].Function : stackFrames[0].Function;
                if (index > 0 && caller is LuaClosure callerClosure)
                {
                    var t = LuaDebug.GetFuncName(callerClosure.Proto, stackFrames[index].CallerInstructionIndex, out var name);
                    if (t is not null)
                    {
                        if (t is "global")
                        {
                            list.AddRange("function '");
                            list.AddRange(name);
                            list.AddRange("'\n");
                        }
                        else
                        {
                            list.AddRange(t);
                            list.AddRange(" '");
                            list.AddRange(name);
                            list.AddRange("'\n");
                        }

                        goto Next;
                    }
                }


                list.AddRange("function <");
                list.AddRange(shortSourceBuffer[..len]);
                list.AddRange(":");
                {
                    p.LineDefined.TryFormat(intFormatBuffer, out var charsWritten, provider: CultureInfo.InvariantCulture);
                    list.AddRange(intFormatBuffer[..charsWritten]);
                    list.AddRange(">\n");
                }

            Next: ;
            }
        }

        return list.AsSpan()[..^1].ToString();
    }
}