using Lua.CodeAnalysis;
using System.Runtime.CompilerServices;
using Lua.Runtime;
using Lua.Internal;

namespace Lua.Standard;

public class DebugLibrary
{
    public static readonly DebugLibrary Instance = new();

    public DebugLibrary()
    {
        var libraryName = "debug";
        Functions =
        [
            new(libraryName, "getlocal", GetLocal),
            new(libraryName, "setlocal", SetLocal),
            new(libraryName, "getupvalue", GetUpValue),
            new(libraryName, "setupvalue", SetUpValue),
            new(libraryName, "getmetatable", GetMetatable),
            new(libraryName, "setmetatable", SetMetatable),
            new(libraryName, "getuservalue", GetUserValue),
            new(libraryName, "setuservalue", SetUserValue),
            new(libraryName, "traceback", Traceback),
            new(libraryName, "getregistry", GetRegistry),
            new(libraryName, "upvalueid", UpValueId),
            new(libraryName, "upvaluejoin", UpValueJoin),
            new(libraryName, "gethook", GetHook),
            new(libraryName, "sethook", SetHook),
            new(libraryName, "getinfo", GetInfo)
        ];
    }

    public readonly LibraryFunction[] Functions;


    static LuaState GetLuaThread(in LuaFunctionExecutionContext context, out int argOffset)
    {
        if (context.ArgumentCount < 1)
        {
            argOffset = 0;
            return context.State;
        }

        if (context.GetArgument(0).TryRead<LuaState>(out var state))
        {
            argOffset = 1;
            return state;
        }

        argOffset = 0;
        return context.State;
    }


    static ref LuaValue FindLocal(LuaState state, int level, int index, out string? name)
    {
        if (index == 0)
        {
            name = null;
            return ref Unsafe.NullRef<LuaValue>();
        }

        var callStack = state.GetCallStackFrames();
        var frame = callStack[^(level + 1)];
        if (index < 0)
        {
            index = -index - 1;
            var frameVariableArgumentCount = frame.VariableArgumentCount;
            if (frameVariableArgumentCount > 0 && index < frameVariableArgumentCount)
            {
                name = "(*vararg)";
                return ref state.Stack.Get(frame.Base - frameVariableArgumentCount + index);
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
                var nextFrameBase = closure.Proto.Code[currentPc].OpCode is OpCode.Call or OpCode.TailCall ? nextFrame.Base - 1 : nextFrame.Base;
                if (nextFrameBase - 1 < frameBase + index)
                {
                    name = null;
                    return ref Unsafe.NullRef<LuaValue>();
                }
            }

            var localId = index + 1;
            foreach (var l in locals)
            {
                if (currentPc < l.StartPc)
                {
                    break;
                }

                if (l.EndPc <= currentPc)
                {
                    continue;
                }

                localId--;
                if (localId == 0)
                {
                    name = l.Name;
                    return ref state.Stack.Get(frameBase + index);
                }
            }
        }
        else
        {
            var nextFrameBase = level != 0 ? callStack[^level].Base : state.Stack.Count;

            if (nextFrameBase - 1 < frameBase + index)
            {
                name = null;
                return ref Unsafe.NullRef<LuaValue>();
            }
        }

        name = "(*temporary)";
        return ref state.Stack.Get(frameBase + index);
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

        var state = GetLuaThread(context, out var argOffset);

        var index = context.GetArgument<int>(argOffset + 1);
        if (context.GetArgument(argOffset).TryReadFunction(out var f))
        {
            return new(context.Return(GetParam(f, index - 1)));
        }

        var level = context.GetArgument<int>(argOffset);


        if (level < 0 || level >= state.GetCallStackFrames().Length)
        {
            context.ThrowBadArgument(1, "level out of range");
        }

        ref var local = ref FindLocal(state, level, index, out var name);
        if (name is null)
        {
            return new(context.Return(LuaValue.Nil));
        }

        return new(context.Return(name, local));
    }

    public ValueTask<int> SetLocal(LuaFunctionExecutionContext context, CancellationToken cancellationToken)
    {
        var state = GetLuaThread(context, out var argOffset);

        var value = context.GetArgument(argOffset + 2);
        var index = context.GetArgument<int>(argOffset + 1);
        var level = context.GetArgument<int>(argOffset);


        if (level < 0 || level >= state.GetCallStackFrames().Length)
        {
            context.ThrowBadArgument(1, "level out of range");
        }

        ref var local = ref FindLocal(state, level, index, out var name);
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

        if (context.GlobalState.TryGetMetatable(arg0, out var table))
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
            LuaRuntimeException.BadArgument(context.State, 2, [LuaValueType.Nil, LuaValueType.Table], arg1.Type);
        }

        context.GlobalState.SetMetatable(arg0, arg1.UnsafeRead<LuaTable>());

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
        var state = GetLuaThread(context, out var argOffset);

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

        if (state is { IsCoroutine: true, LuaTraceback: {} traceback })
        {
            return new(context.Return(traceback.ToString(level)));
        }

        var callStack = state.GetCallStackFrames();
        if (callStack.Length == 0)
        {
            return new(context.Return("stack traceback:"));
        }

        var skipCount = Math.Min(Math.Max(level - 1, 0), callStack.Length - 1);
        var frames = callStack[..^skipCount];
        return new(context.Return(Runtime.Traceback.CreateTracebackMessage(context.GlobalState, frames, message, level == 1 ? 1 : 0)));
    }

    public ValueTask<int> GetRegistry(LuaFunctionExecutionContext context, CancellationToken cancellationToken)
    {
        return new(context.Return(context.GlobalState.Registry));
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

        return new(context.Return(LuaValue.FromObject(upValues[n1 - 1])));
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
        var state = GetLuaThread(context, out var argOffset);
        var hook = context.GetArgumentOrDefault<LuaFunction?>(argOffset);
        var mask = context.GetArgumentOrDefault<string?>(argOffset + 1) ?? "";
        var count = context.GetArgumentOrDefault<int>(argOffset + 2);
        state.SetHook(hook, mask, count);
        if (hook is null)
        {
            return 0;
        }

        if (state.IsReturnHookEnabled && context.State == state)
        {
            var stack = state.Stack;
            var top = stack.Count;
            stack.Push("return");
            stack.Push(LuaValue.Nil);
            context.State.IsInHook = true;
            var frame = context.State.CreateCallStackFrame(hook, 2, top, 0);
            context.State.PushCallStackFrame(frame);
            LuaFunctionExecutionContext funcContext = new() { State = context.State, ArgumentCount = stack.Count - frame.Base, ReturnFrameBase = frame.ReturnBase };
            try
            {
                await hook.Func(funcContext, cancellationToken);
            }
            finally
            {
                context.State.IsInHook = false;
                context.State.PopCallStackFrameWithStackPop();
            }
        }

        return 0;
    }

    public ValueTask<int> GetHook(LuaFunctionExecutionContext context, CancellationToken cancellationToken)
    {
        var state = GetLuaThread(context, out var argOffset);
        if (state.Hook is null)
        {
            return new(context.Return(LuaValue.Nil, LuaValue.Nil, LuaValue.Nil));
        }

        return new(context.Return(state.Hook,
            (state.IsCallHookEnabled ? "c" : "") +
            (state.IsReturnHookEnabled ? "r" : "") +
            (state.IsLineHookEnabled ? "l" : "")
            , state.BaseHookCount));
    }

    public ValueTask<int> GetInfo(LuaFunctionExecutionContext context, CancellationToken cancellationToken)
    {
        //return new(0);
        var state = GetLuaThread(context, out var argOffset);
        var what = context.GetArgumentOrDefault<string>(argOffset + 1, "flnStu");
        CallStackFrame? previousFrame = null;
        CallStackFrame? currentFrame = null;
        var pc = 0;
        var arg1 = context.GetArgument(argOffset);

        if (arg1.TryReadFunction(out var functionToInspect))
        {
            //what = ">" + what;
        }
        else if (arg1.TryReadNumber(out _))
        {
            var level = context.GetArgument<int>(argOffset) + 1;

            var callStack = state.GetCallStackFrames();

            if (level <= 0 || level > callStack.Length)
            {
                return new(context.Return(LuaValue.Nil));
            }


            currentFrame = state.GetCallStackFrames()[^level];
            previousFrame = level + 1 <= callStack.Length ? callStack[^(level + 1)] : null;
            if (level != 1)
            {
                pc = state.GetCallStackFrames()[^(level - 1)].CallerInstructionIndex;
            }

            functionToInspect = currentFrame.Value.Function;
        }
        else
        {
            context.ThrowBadArgument(argOffset, "function or level expected");
        }

        using var debug = LuaDebug.Create(context.GlobalState, previousFrame, currentFrame, functionToInspect, pc, what, out var isValid);
        if (!isValid)
        {
            context.ThrowBadArgument(argOffset + 1, "invalid option");
        }

        LuaTable table = new(0, 1);
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
                LuaTable activeLines = new(0, 8);
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