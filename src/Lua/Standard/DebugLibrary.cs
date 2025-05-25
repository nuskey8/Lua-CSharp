using System.Runtime.CompilerServices;
using Lua.Runtime;
using Lua.Internal;

namespace Lua.Standard;

public class DebugLibrary
{
    public static readonly DebugLibrary Instance = new();

    public DebugLibrary()
    {
        Functions =
        [
            new("getlocal", GetLocal),
            new("setlocal", SetLocal),
            new("getupvalue", GetUpValue),
            new("setupvalue", SetUpValue),
            new("getmetatable", GetMetatable),
            new("setmetatable", SetMetatable),
            new("getuservalue", GetUserValue),
            new("setuservalue", SetUserValue),
            new("traceback", Traceback),
            new("getregistry", GetRegistry),
            new("upvalueid", UpValueId),
            new("upvaluejoin", UpValueJoin),
            new("gethook", GetHook),
            new("sethook", SetHook),
            new("getinfo", GetInfo),
        ];
    }

    public readonly LuaFunction[] Functions;


    static LuaThread GetLuaThread(in LuaFunctionExecutionContext context, out int argOffset)
    {
        if (context.ArgumentCount < 1)
        {
            argOffset = 0;
            return context.Thread;
        }

        if (context.GetArgument(0).TryRead<LuaThread>(out var thread))
        {
            argOffset = 1;
            return thread;
        }

        argOffset = 0;
        return context.Thread;
    }


    static ref LuaValue FindLocal(LuaThread thread, int level, int index, out string? name)
    {
        if (index == 0)
        {
            name = null;
            return ref Unsafe.NullRef<LuaValue>();
        }

        var callStack = thread.GetCallStackFrames();
        var frame = callStack[^(level + 1)];
        if (index < 0)
        {
            index = -index - 1;
            var frameVariableArgumentCount = frame.VariableArgumentCount;
            if (frameVariableArgumentCount > 0 && index < frameVariableArgumentCount)
            {
                name = "(*vararg)";
                return ref thread.Stack.Get(frame.Base - frameVariableArgumentCount + index);
            }

            name = null;
            return ref Unsafe.NullRef<LuaValue>();
        }

        index -= 1;


        var frameBase = frame.Base;


        if (frame.Function is LuaClosure closure)
        {
            var locals = closure.Proto.LocalVariables;
            var nextFrame = callStack[^level];
            var currentPc = nextFrame.CallerInstructionIndex;
            {
                int nextFrameBase = (closure.Proto.Code[currentPc].OpCode is OpCode.Call or OpCode.TailCall) ? nextFrame.Base - 1 : nextFrame.Base;
                if (nextFrameBase - 1 < frameBase + index)
                {
                    name = null;
                    return ref Unsafe.NullRef<LuaValue>();
                }
            }

            var localId = index + 1;
            foreach (var l in locals)
            {
                if (currentPc < l.StartPc) break;
                if (l.EndPc <= currentPc) continue;
                localId--;
                if (localId == 0)
                {
                    name = l.Name;
                    return ref thread.Stack.Get(frameBase + index);
                }
            }
        }
        else
        {
            int nextFrameBase = level != 0 ? callStack[^level].Base : thread.Stack.Count;

            if (nextFrameBase - 1 < frameBase + index)
            {
                name = null;
                return ref Unsafe.NullRef<LuaValue>();
            }
        }

        name = "(*temporary)";
        return ref thread.Stack.Get(frameBase + index);
    }

    public ValueTask<int> GetLocal(LuaFunctionExecutionContext context, CancellationToken cancellationToken)
    {
        static LuaValue GetParam(LuaFunction function, int index)
        {
            if (function is LuaClosure closure)
            {
                var paramCount = closure.Proto.ParameterCount;
                if (0 <= index && index < paramCount)
                {
                    return closure.Proto.LocalVariables[index].Name;
                }
            }

            return LuaValue.Nil;
        }

        var thread = GetLuaThread(context, out var argOffset);

        var index = context.GetArgument<int>(argOffset + 1);
        if (context.GetArgument(argOffset).TryReadFunction(out var f))
        {
            return new(context.Return(GetParam(f, index - 1)));
        }

        var level = context.GetArgument<int>(argOffset);


        if (level < 0 || level >= thread.GetCallStackFrames().Length)
        {
            context.ThrowBadArgument(1, "level out of range");
        }

        ref var local = ref FindLocal(thread, level, index, out var name);
        if (name is null)
        {
            return new(context.Return(LuaValue.Nil));
        }

        return new(context.Return(name, local));
    }

    public ValueTask<int> SetLocal(LuaFunctionExecutionContext context, CancellationToken cancellationToken)
    {
        var thread = GetLuaThread(context, out var argOffset);

        var value = context.GetArgument(argOffset + 2);
        var index = context.GetArgument<int>(argOffset + 1);
        var level = context.GetArgument<int>(argOffset);


        if (level < 0 || level >= thread.GetCallStackFrames().Length)
        {
            context.ThrowBadArgument(1, "level out of range");
        }

        ref var local = ref FindLocal(thread, level, index, out var name);
        if (name is null)
        {
            return new(context.Return(LuaValue.Nil));
        }

        local = value;
        return new(context.Return(name));
    }

    public ValueTask<int> GetUpValue(LuaFunctionExecutionContext context, CancellationToken cancellationToken)
    {
        var func = context.GetArgument<LuaFunction>(0);
        var index = context.GetArgument<int>(1) - 1;
        if (func is not LuaClosure closure)
        {
            if (func is CSharpClosure csClosure)
            {
                var upValues = csClosure.UpValues;
                if (index < 0 || index >= upValues.Length)
                {
                    return new(context.Return());
                }

                return new(context.Return("", upValues[index]));
            }

            return new(context.Return());
        }

        {
            var upValues = closure.UpValues;
            var descriptions = closure.Proto.UpValues;
            if (index < 0 || index >= descriptions.Length)
            {
                return new(context.Return());
            }

            var description = descriptions[index];
            return new(context.Return(description.Name.ToString(), upValues[index].GetValue()));
        }
    }

    public ValueTask<int> SetUpValue(LuaFunctionExecutionContext context, CancellationToken cancellationToken)
    {
        var func = context.GetArgument<LuaFunction>(0);
        var index = context.GetArgument<int>(1) - 1;
        var value = context.GetArgument(2);
        if (func is not LuaClosure closure)
        {
            if (func is CSharpClosure csClosure)
            {
                var upValues = csClosure.UpValues;
                if (index >= 0 && index < upValues.Length)
                {
                    upValues[index] = value;
                    return new(context.Return(""));
                }
            }

            return new(context.Return());
        }

        {
            var upValues = closure.UpValues;
            var descriptions = closure.Proto.UpValues;
            if (index < 0 || index >= descriptions.Length)
            {
                return new(context.Return());
            }

            var description = descriptions[index];
            upValues[index].SetValue(value);
            return new(context.Return(description.Name.ToString()));
        }
    }

    public ValueTask<int> GetMetatable(LuaFunctionExecutionContext context, CancellationToken cancellationToken)
    {
        var arg0 = context.GetArgument(0);

        if (context.State.TryGetMetatable(arg0, out var table))
        {
            return new(context.Return(table));
        }
        else
        {
            return new(context.Return(LuaValue.Nil));
        }
    }

    public ValueTask<int> SetMetatable(LuaFunctionExecutionContext context, CancellationToken cancellationToken)
    {
        var arg0 = context.GetArgument(0);
        var arg1 = context.GetArgument(1);

        if (arg1.Type is not (LuaValueType.Nil or LuaValueType.Table))
        {
            LuaRuntimeException.BadArgument(context.Thread, 2, "setmetatable", [LuaValueType.Nil, LuaValueType.Table]);
        }

        context.State.SetMetatable(arg0, arg1.UnsafeRead<LuaTable>());

        return new(context.Return(arg0));
    }

    public ValueTask<int> GetUserValue(LuaFunctionExecutionContext context, CancellationToken cancellationToken)
    {
        if (!context.GetArgumentOrDefault(0).TryRead<ILuaUserData>(out var iUserData))
        {
            return new(context.Return(LuaValue.Nil));
        }

        var index = 1; // context.GetArgument<int>(1); //for lua 5.4
        var userValues = iUserData.UserValues;
        if (index > userValues.Length
            //index < 1 ||  // for lua 5.4
           )
        {
            return new(context.Return(LuaValue.Nil));
        }

        return new(context.Return(userValues[index - 1]));
    }

    public ValueTask<int> SetUserValue(LuaFunctionExecutionContext context, CancellationToken cancellationToken)
    {
        var iUserData = context.GetArgument<ILuaUserData>(0);
        var value = context.GetArgument(1);
        var index = 1; // context.GetArgument<int>(2);// for lua 5.4
        var userValues = iUserData.UserValues;
        if (index > userValues.Length
            //|| index < 1 // for lua 5.4
           )
        {
            return new(context.Return(LuaValue.Nil));
        }

        userValues[index - 1] = value;
        return new(context.Return(new LuaValue(iUserData)));
    }

    public ValueTask<int> Traceback(LuaFunctionExecutionContext context, CancellationToken cancellationToken)
    {
        var thread = GetLuaThread(context, out var argOffset);

        var message = context.GetArgumentOrDefault(argOffset);
        var level = context.GetArgumentOrDefault<int>(argOffset + 1, argOffset == 0 ? 1 : 0);

        if (message.Type is not (LuaValueType.Nil or LuaValueType.String or LuaValueType.Number))
        {
            return new(context.Return(message));
        }

        if (level < 0)
        {
            return new(context.Return(LuaValue.Nil));
        }

        if (thread is LuaCoroutine coroutine)
        {
            if (coroutine.LuaTraceback is not null)
            {
                return new(context.Return(coroutine.LuaTraceback.ToString(level)));
            }
        }

        var callStack = thread.GetCallStackFrames();
        if (callStack.Length == 0)
        {
            return new(context.Return("stack traceback:"));
        }

        var skipCount = Math.Min(Math.Max(level - 1, 0), callStack.Length - 1);
        var frames = callStack[..^skipCount];
        return new(context.Return(Runtime.Traceback.GetTracebackString(context.State, frames, message, level == 1)));
    }

    public ValueTask<int> GetRegistry(LuaFunctionExecutionContext context, CancellationToken cancellationToken)
    {
        return new(context.Return(context.State.Registry));
    }

    public ValueTask<int> UpValueId(LuaFunctionExecutionContext context, CancellationToken cancellationToken)
    {
        var n1 = context.GetArgument<int>(1);
        var f1 = context.GetArgument<LuaFunction>(0);

        if (f1 is not LuaClosure closure)
        {
            return new(context.Return(LuaValue.Nil));
        }

        var upValues = closure.GetUpValuesSpan();
        if (n1 <= 0 || n1 > upValues.Length)
        {
            return new(context.Return(LuaValue.Nil));
        }

        return new(context.Return( LuaValue.FromObject(upValues[n1 - 1])));
    }

    public ValueTask<int> UpValueJoin(LuaFunctionExecutionContext context, CancellationToken cancellationToken)
    {
        var n2 = context.GetArgument<int>(3);
        var f2 = context.GetArgument<LuaFunction>(2);
        var n1 = context.GetArgument<int>(1);
        var f1 = context.GetArgument<LuaFunction>(0);

        if (f1 is not LuaClosure closure1 || f2 is not LuaClosure closure2)
        {
            return new(context.Return(LuaValue.Nil));
        }

        var upValues1 = closure1.GetUpValuesSpan();
        var upValues2 = closure2.GetUpValuesSpan();
        if (n1 <= 0 || n1 > upValues1.Length)
        {
            context.ThrowBadArgument(1, "invalid upvalue index");
        }

        if (n2 < 0 || n2 > upValues2.Length)
        {
            context.ThrowBadArgument(3, "invalid upvalue index");
        }

        upValues1[n1 - 1] = upValues2[n2 - 1];
        return new(0);
    }

    public async ValueTask<int> SetHook(LuaFunctionExecutionContext context, CancellationToken cancellationToken)
    {
        var thread = GetLuaThread(context, out var argOffset);
        LuaFunction? hook = context.GetArgumentOrDefault<LuaFunction?>(argOffset);
        var mask = context.GetArgumentOrDefault<string?>(argOffset + 1)??"";
        var count = context.GetArgumentOrDefault<int>(argOffset + 2);
        thread.SetHook(hook, mask, count);
        if (hook is null)
        {
            return 0;
        }

        if (thread.IsReturnHookEnabled && context.Thread == thread)
        {
            var stack = thread.Stack;
            var top = stack.Count;
            stack.Push("return");
            stack.Push(LuaValue.Nil);
            context.Thread.IsInHook = true;
            var frame = context.Thread.CurrentAccess.CreateCallStackFrame(hook, 2, top, 0);
            var access = context.Thread.PushCallStackFrame(frame);
            var funcContext = new LuaFunctionExecutionContext { Access = access, ArgumentCount = stack.Count - frame.Base, ReturnFrameBase = frame.ReturnBase };
            try
            {
                await hook.Func(funcContext, cancellationToken);
            }
            finally
            {
                context.Thread.IsInHook = false;
                context.Thread.PopCallStackFrameWithStackPop();
            }
        }

        return 0;
    }

    public ValueTask<int> GetHook(LuaFunctionExecutionContext context, CancellationToken cancellationToken)
    {
        var thread = GetLuaThread(context, out var argOffset);
        if (thread.Hook is null)
        {
            return new(context.Return(LuaValue.Nil, LuaValue.Nil, LuaValue.Nil));
        }

        return new(context.Return(thread.Hook,
            (
                (thread.IsCallHookEnabled ? "c" : "") +
                (thread.IsReturnHookEnabled ? "r" : "") +
                (thread.IsLineHookEnabled ? "l" : "")
            )
            , thread.BaseHookCount));
    }

    public ValueTask<int> GetInfo(LuaFunctionExecutionContext context, CancellationToken cancellationToken)
    {
        //return new(0);
        var thread = GetLuaThread(context, out var argOffset);
        string what = context.GetArgumentOrDefault<string>(argOffset + 1, "flnStu");
        CallStackFrame? previousFrame = null;
        CallStackFrame? currentFrame = null;
        int pc = 0;
        var arg1 = context.GetArgument(argOffset);

        if (arg1.TryReadFunction(out var functionToInspect))
        {
            //what = ">" + what;
        }
        else if (arg1.TryReadNumber(out _))
        {
            var level = context.GetArgument<int>(argOffset) + 1;

            var callStack = thread.GetCallStackFrames();

            if (level <= 0 || level > callStack.Length)
            {
                return new(context.Return(LuaValue.Nil));
            }


            currentFrame = thread.GetCallStackFrames()[^(level)];
            previousFrame = level + 1 <= callStack.Length ? callStack[^(level + 1)] : null;
            if (level != 1)
            {
                pc = thread.GetCallStackFrames()[^(level - 1)].CallerInstructionIndex;
            }

            functionToInspect = currentFrame.Value.Function;
        }
        else
        {
            context.ThrowBadArgument(argOffset, "function or level expected");
        }

        using var debug = LuaDebug.Create(context.State, previousFrame, currentFrame, functionToInspect, pc, what, out var isValid);
        if (!isValid)
        {
            context.ThrowBadArgument(argOffset + 1, "invalid option");
        }

        var table = new LuaTable(0, 1);
        if (what.Contains('S'))
        {
            table["source"] = debug.Source ?? LuaValue.Nil;
            table["short_src"] = debug.ShortSource.ToString();
            table["linedefined"] = debug.LineDefined;
            table["lastlinedefined"] = debug.LastLineDefined;
            table["what"] = debug.What ?? LuaValue.Nil;
            ;
        }

        if (what.Contains('l'))
        {
            table["currentline"] = debug.CurrentLine;
        }

        if (what.Contains('u'))
        {
            table["nups"] = debug.UpValueCount;
            table["nparams"] = debug.ParameterCount;
            table["isvararg"] = debug.IsVarArg;
        }

        if (what.Contains('n'))
        {
            table["name"] = debug.Name ?? LuaValue.Nil;
            table["namewhat"] = debug.NameWhat ?? LuaValue.Nil;
        }

        if (what.Contains('t'))
        {
            table["istailcall"] = debug.IsTailCall;
        }

        if (what.Contains('f'))
        {
            table["func"] = functionToInspect;
        }

        if (what.Contains('L'))
        {
            if (functionToInspect is LuaClosure closure)
            {
                var activeLines = new LuaTable(0, 8);
                foreach (var line in closure.Proto.LineInfo)
                {
                    activeLines[line] = true;
                }

                table["activelines"] = activeLines;
            }
        }

        return new(context.Return(table));
    }
}