using System.Runtime.CompilerServices;
using Lua.CodeAnalysis;
using Lua.CodeAnalysis.Syntax;
using Lua.Internal;
using Lua.Runtime;

namespace Lua;

public class LuaParseException(string? chunkName, SourcePosition position, string message) : Exception(message)
{
    public string? ChunkName { get; } = chunkName;
    public SourcePosition Position { get; } = position;

    public static void UnexpectedToken(string? chunkName, SourcePosition position, SyntaxToken token)
    {
        throw new LuaParseException(chunkName, position, $"unexpected symbol <{token.Type}> near '{token.Text}'");
    }

    public static void ExpectedToken(string? chunkName, SourcePosition position, SyntaxTokenType token)
    {
        throw new LuaParseException(chunkName, position, $"'{token}' expected");
    }

    public static void UnfinishedLongComment(string? chunkName, SourcePosition position)
    {
        throw new LuaParseException(chunkName, position, $"unfinished long comment (starting at line {position.Line})");
    }

    public static void SyntaxError(string? chunkName, SourcePosition position, SyntaxToken? token)
    {
        throw new LuaParseException(chunkName, position, $"syntax error {(token == null ? "" : $"near '{token.Value.Text}'")}");
    }

    public static void NoVisibleLabel(string label, string? chunkName, SourcePosition position)
    {
        throw new LuaParseException(chunkName, position, $"no visible label '{label}' for <goto>");
    }

    public static void BreakNotInsideALoop(string? chunkName, SourcePosition position)
    {
        throw new LuaParseException(chunkName, position, "<break> not inside a loop");
    }

    public override string Message => $"{ChunkName}:{Position.Line}: {base.Message}";
}

public class LuaCompileException(string chunkName, SourcePosition position, int offset, string message, string? nearToken) : Exception(GetMessageWithNearToken(message, nearToken))
{
    public string ChunkName { get; } = chunkName;
    public int OffSet { get; } = offset;

    public SourcePosition Position => position;

    public string MainMessage => message;

    public string? NearToken => nearToken;

    public string MessageWithNearToken => base.Message;

    public override string Message => $"{ChunkName}:{Position.Line}: {base.Message}";

    static string GetMessageWithNearToken(string message, string? nearToken)
    {
        if (string.IsNullOrEmpty(nearToken))
        {
            return message;
        }

        return $"{message} near {nearToken}";
    }
}

public class LuaUndumpException(string message) : Exception(message);

class LuaStackOverflowException() : Exception("stack overflow")
{
    public override string ToString()
    {
        return "stack overflow";
    }
}

interface ILuaTracebackBuildable
{
    Traceback? BuildOrGet();
}

public class LuaRuntimeException : Exception, ILuaTracebackBuildable
{
    public LuaRuntimeException(LuaState? state, Exception innerException) : base(innerException.Message, innerException)
    {
        if (state != null)
        {
            state.CurrentException?.BuildOrGet();
            state.ExceptionTrace.Clear();
            state.CurrentException = this;
        }

        State = state;
    }

    public LuaRuntimeException(LuaState? state, LuaValue errorObject, int level = 1)
    {
        if (state != null)
        {
            state.CurrentException?.BuildOrGet();
            state.ExceptionTrace.Clear();
            state.CurrentException = this;
        }

        State = state;

        ErrorObject = errorObject;
        this.level = level;
    }

    int level = 1;

    Traceback? luaTraceback;

    public Traceback? LuaTraceback
    {
        get
        {
            if (luaTraceback == null)
            {
                ((ILuaTracebackBuildable)this).BuildOrGet();
            }

            return luaTraceback;
        }
    }

    internal LuaState? State { get; private set; } = default!;
    public LuaValue ErrorObject { get; }

    public static void AttemptInvalidOperation(LuaState? state, string op, LuaValue a, LuaValue b)
    {
        var typeA = a.TypeToString();
        var typeB = b.TypeToString();
        if (typeA == typeB)
        {
            throw new LuaRuntimeException(state, $"attempt to {op} two {typeA} values");
        }

        throw new LuaRuntimeException(state, $"attempt to {op} a {typeA} value with a {typeB} value");
    }

    public static void AttemptInvalidOperation(LuaState? state, string op, LuaValue a)
    {
        throw new LuaRuntimeException(state, $"attempt to {op} a {a.TypeToString()} value");
    }

    internal static void AttemptInvalidOperationOnLuaStack(LuaState state, string op, int lastPc, int regA, int regB)
    {
        var caller = state.GetCurrentFrame();
        var luaValueA = regA < 255 ? state.Stack[caller.Base + regA] : ((LuaClosure)caller.Function).Proto.Constants[regA - 256];
        var luaValueB = regB < 255 ? state.Stack[caller.Base + regB] : ((LuaClosure)caller.Function).Proto.Constants[regB - 256];
        var function = caller.Function;
        var tA = LuaDebug.GetName(((LuaClosure)function).Proto, lastPc, regA, out var nameA);
        var tB = LuaDebug.GetName(((LuaClosure)function).Proto, lastPc, regB, out var nameB);

        using var builder = new PooledList<char>(64);
        builder.Clear();
        builder.AddRange("attempt to ");
        builder.AddRange(op);
        builder.AddRange(" a ");
        builder.AddRange(luaValueA.TypeToString());
        builder.AddRange(" value");
        if (tA != null && nameA != null)
        {
            builder.AddRange($" ({tA} '{nameA}')");
        }

        builder.AddRange(" with a ");
        builder.AddRange(luaValueB.TypeToString());
        builder.AddRange(" value");
        if (tB != null && nameB != null)
        {
            builder.AddRange($" ({tB} '{nameB}')");
        }

        throw new LuaRuntimeException(state, builder.AsSpan().ToString());
    }

    internal static void AttemptInvalidOperationOnLuaStack(LuaState state, string op, int lastPc, int reg)
    {
        var caller = state.GetCurrentFrame();
        var luaValue = reg < 255 ? state.Stack[caller.Base + reg] : ((LuaClosure)caller.Function).Proto.Constants[reg - 256];
        var function = caller.Function;
        var t = LuaDebug.GetName(((LuaClosure)function).Proto, lastPc, reg, out var name);

        using var builder = new PooledList<char>(64);
        builder.Clear();
        builder.AddRange("attempt to ");
        builder.AddRange(op);
        builder.AddRange(" a ");
        builder.AddRange(luaValue.TypeToString());
        builder.AddRange(" value");
        if (t != null && name != null)
        {
            builder.AddRange($" ({t} '{name}')");
        }

        throw new LuaRuntimeException(state, builder.AsSpan().ToString());
    }

    internal static void AttemptInvalidOperationOnUpValues(LuaState state, string op, int reg)
    {
        var caller = state.GetCurrentFrame();
        var closure = (LuaClosure)caller.Function;
        var proto = closure.Proto;

        var upValue = proto.UpValues[reg];
        var luaValue = closure.UpValues[upValue.Index].GetValue();
        var name = upValue.Name;

        throw new LuaRuntimeException(state, $"attempt to {op} a {luaValue.TypeToString()} value (upvalue '{name}')");
    }

    internal static (string NameWhat, string Name) GetCurrentFunctionName(LuaState state)
    {
        var current = state.GetCurrentFrame();
        var pc = current.CallerInstructionIndex;
        LuaFunction callerFunction;
        if (current.IsTailCall)
        {
            pc = state.LastPc;
            callerFunction = state.LastCallerFunction!;
        }
        else
        {
            var caller = state.GetCallStackFrames()[^2];
            callerFunction = caller.Function;
        }

        if (callerFunction is not LuaClosure callerClosure)
        {
            return ("function", current.Function.Name);
        }

        return (LuaDebug.GetFuncName(callerClosure.Proto, pc, out var name) ?? "", name ?? current.Function.Name);
    }

    public static void BadArgument(LuaState state, int argumentId)
    {
        BadArgument(state, argumentId, "value expected");
    }


    public static void BadArgument(LuaState state, int argumentId, LuaValueType expected, LuaValueType actual)
    {
        BadArgument(state, argumentId, $"{LuaValue.ToString(expected)} expected, got {LuaValue.ToString(actual)})");
    }

    public static void BadArgument(LuaState state, int argumentId, LuaValueType[] expected, LuaValueType actual)
    {
        BadArgument(state, argumentId, $"({string.Join(" or ", expected.Select(LuaValue.ToString))} expected, got {LuaValue.ToString(actual)})");
    }


    public static void BadArgument(LuaState state, int argumentId, string expected, string actual)
    {
        BadArgument(state, argumentId, $"({expected} expected, got {actual})");
    }

    public static void BadArgument(LuaState state, int argumentId, string[] expected, string actual)
    {
        if (expected.Length == 0)
        {
            throw new ArgumentException("Expected array must not be empty", nameof(expected));
        }

        BadArgument(state, argumentId, $"({string.Join(" or ", expected)} expected, got {actual})");
    }


    public static void BadArgument(LuaState state, int argumentId, string message)
    {
        var (nameWhat, name) = GetCurrentFunctionName(state);
        if (nameWhat == "method")
        {
            argumentId--;
            if (argumentId == 0)
            {
                throw new LuaRuntimeException(state, $"calling '{name}' on bad self ({message})");
            }
        }

        throw new LuaRuntimeException(state, $"bad argument #{argumentId} to '{name}' ({message})");
    }

    public static void BadArgumentNumberIsNotInteger(LuaState state, int argumentId)
    {
        BadArgument(state, argumentId, "number has no integer representation");
    }

    public static void ThrowBadArgumentIfNumberIsNotInteger(LuaState state, int argumentId, double value)
    {
        if (!MathEx.IsInteger(value))
        {
            BadArgumentNumberIsNotInteger(state, argumentId);
        }
    }

    static string CreateMessage(Traceback traceback, LuaValue errorObject, int level)
    {
        var pooledList = new PooledList<char>(64);
        pooledList.Clear();
        try
        {
            pooledList.AddRange("Lua-CSharp: ");
            traceback.WriteLastLuaTrace(ref pooledList, level);
            pooledList.AddRange($"{errorObject}");
            return pooledList.AsSpan().ToString();
        }
        finally
        {
            pooledList.Dispose();
        }
    }


    [MethodImpl(MethodImplOptions.NoInlining)]
    Traceback? ILuaTracebackBuildable.BuildOrGet()
    {
        if (luaTraceback != null)
        {
            return luaTraceback;
        }

        if (State != null)
        {
            var callStack = State.ExceptionTrace.AsSpan();
            if (callStack.IsEmpty)
            {
                return null;
            }

            luaTraceback = new(State, callStack);
            State.ExceptionTrace.Clear();
            State = null;
        }

        return luaTraceback;
    }

    internal void Forget()
    {
        State?.ExceptionTrace.Clear();
        State = null;
    }

    internal string MinimalMessage()
    {
        var message = InnerException?.ToString() ?? ErrorObject.ToString();
        if (level <= 0)
        {
            return message;
        }

        if (luaTraceback == null)
        {
            if (State != null)
            {
                var callStack = State.ExceptionTrace.AsSpan();
                level = Math.Min(level, callStack.Length + 1);
                callStack = callStack[..^(level - 1)];
                if (callStack.IsEmpty)
                {
                    return ErrorObject.ToString();
                }

                {
                    var pooledList = new PooledList<char>(64);
                    pooledList.Clear();
                    try
                    {
                        Traceback.WriteLastLuaTrace(callStack, ref pooledList);
                        pooledList.AddRange(message);
                        return pooledList.AsSpan().ToString();
                    }
                    finally
                    {
                        pooledList.Dispose();
                    }
                }
            }

            return message;
        }

        {
            var pooledList = new PooledList<char>(64);
            pooledList.Clear();
            try
            {
                luaTraceback.WriteLastLuaTrace(ref pooledList, level);
                pooledList.AddRange(message);
                return pooledList.AsSpan().ToString();
            }
            finally
            {
                pooledList.Dispose();
            }
        }
    }

    public override string Message
    {
        get
        {
            if (InnerException != null)
            {
                return InnerException.Message;
            }

            if (LuaTraceback == null)
            {
                return ErrorObject.ToString();
            }

            return CreateMessage(LuaTraceback, ErrorObject, level);
        }
    }

    public override string ToString()
    {
        if (LuaTraceback == null)
        {
            return base.ToString();
        }

        var pooledList = new PooledList<char>(64);
        pooledList.Clear();
        try
        {
            pooledList.AddRange(Message);
            pooledList.Add('\n');
            pooledList.AddRange(LuaTraceback.ToString());
            pooledList.Add('\n');
            pooledList.AddRange(StackTrace);
            return pooledList.AsSpan().ToString();
        }
        finally
        {
            pooledList.Dispose();
        }
    }
}

public class LuaAssertionException(LuaState? traceback, string message) : LuaRuntimeException(traceback, message);

public class LuaModuleNotFoundException(string moduleName) : Exception($"module '{moduleName}' not found");

public sealed class LuaCanceledException : OperationCanceledException, ILuaTracebackBuildable
{
    Traceback? luaTraceback;

    public Traceback? LuaTraceback
    {
        get
        {
            if (luaTraceback == null)
            {
                ((ILuaTracebackBuildable)this).BuildOrGet();
            }

            return luaTraceback;
        }
    }

    internal LuaState? State { get; private set; }

    internal LuaCanceledException(LuaState state, CancellationToken cancellationToken, Exception? innerException = null) : base("The operation was cancelled during execution on Lua.", innerException, cancellationToken)
    {
        state.CurrentException?.BuildOrGet();
        state.ExceptionTrace.Clear();
        state.CurrentException = this;
        State = state;
    }


    [MethodImpl(MethodImplOptions.NoInlining)]
    Traceback? ILuaTracebackBuildable.BuildOrGet()
    {
        if (luaTraceback != null)
        {
            return luaTraceback;
        }

        if (State != null)
        {
            var callStack = State.ExceptionTrace.AsSpan();
            if (callStack.IsEmpty)
            {
                return null;
            }

            luaTraceback = new(State, callStack);
            State.ExceptionTrace.Clear();
            State = null!;
        }

        return luaTraceback;
    }
}