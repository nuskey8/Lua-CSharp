using System.Runtime.CompilerServices;
using Lua.CodeAnalysis;
using Lua.CodeAnalysis.Syntax;
using Lua.Internal;
using Lua.Platforms;
using Lua.Runtime;

namespace Lua;

public class LuaParseException(string? chunkName, SourcePosition position, string message)
    : Exception(message)
{
    public string? ChunkName { get; } = chunkName;
    public SourcePosition Position { get; } = position;

    public static void UnexpectedToken(
        string? chunkName,
        SourcePosition position,
        SyntaxToken token
    )
    {
        throw new LuaParseException(
            chunkName,
            position,
            $"unexpected symbol near {GetNearTokenText(token)}"
        );
    }

    public static void ExpectedToken(
        string? chunkName,
        SourcePosition position,
        SyntaxTokenType token
    )
    {
        throw new LuaParseException(chunkName, position, $"{GetTokenTypeText(token)} expected");
    }

    internal static void ExpectedToken(
        string? chunkName,
        SourcePosition position,
        SyntaxTokenType token,
        SyntaxToken? nearToken
    )
    {
        throw new LuaParseException(
            chunkName,
            position,
            $"{GetTokenTypeText(token)} expected near {GetNearTokenText(nearToken)}"
        );
    }

    static string GetTokenTypeText(SyntaxTokenType token)
    {
        // Lua's token2str quotes fixed syntax, but uses unquoted categories for literals/names.
        return token switch
        {
            SyntaxTokenType.Identifier => "<name>",
            SyntaxTokenType.Number => "<number>",
            SyntaxTokenType.String or SyntaxTokenType.RawString => "<string>",
            SyntaxTokenType.EndOfLine => "<eol>",
            _ => SyntaxToken.GetFixedText(token) is { } text
                ? $"'{text}'"
                : $"<{token.ToString().ToLowerInvariant()}>",
        };
    }

    internal static void ExpectedClosingToken(
        string? chunkName,
        SourcePosition position,
        SyntaxTokenType token,
        SyntaxToken openingToken,
        SyntaxToken? nearToken
    )
    {
        // Like Lua's check_match, a different line needs the opening construct's context.
        var context =
            openingToken.Position.Line == position.Line
                ? ""
                : $" (to close {GetTokenTypeText(openingToken.Type)} at line {openingToken.Position.Line})";
        throw new LuaParseException(
            chunkName,
            position,
            $"{GetTokenTypeText(token)} expected{context} near {GetNearTokenText(nearToken)}"
        );
    }

    static string GetNearTokenText(SyntaxToken? token)
    {
        if (token is not { } value)
        {
            return "<eof>";
        }

        return value.Type switch
        {
            SyntaxTokenType.Identifier or SyntaxTokenType.Number => $"'{value.Text}'",
            SyntaxTokenType.String or SyntaxTokenType.RawString => $"'{value.ToDisplayString()}'",
            _ => GetTokenTypeText(value.Type),
        };
    }

    public static void UnfinishedLongComment(string? chunkName, SourcePosition position)
    {
        throw new LuaParseException(
            chunkName,
            position,
            $"unfinished long comment (starting at line {position.Line})"
        );
    }

    public static void SyntaxError(string? chunkName, SourcePosition position, SyntaxToken? token)
    {
        throw new LuaParseException(
            chunkName,
            position,
            $"syntax error near {GetNearTokenText(token)}"
        );
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

public class LuaCompileException(
    string chunkName,
    SourcePosition position,
    int offset,
    string message,
    string? nearToken
) : Exception(GetMessageWithNearToken(message, nearToken))
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

public class LuaStackOverflowException() : Exception("stack overflow")
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
    public LuaRuntimeException(LuaState? state, Exception innerException)
        : base(innerException.Message, innerException)
    {
        if (state is { IsRunning: true })
        {
            state.CurrentException?.BuildOrGet();
            state.ExceptionTrace.Clear();
            state.CurrentException = this;
            State = state;
        }
    }

    public LuaRuntimeException(LuaState? state, LuaValue errorObject, int level = 1)
    {
        if (state is { IsRunning: true })
        {
            state.CurrentException?.BuildOrGet();
            state.ExceptionTrace.Clear();
            state.CurrentException = this;
            State = state;
            if (level == 1)
                CaptureTailCallSite(state);
        }

        ErrorObject = errorObject;
        this.level = level;
    }

    readonly int level = 1;

    // Where the discarded caller of a native tail call made the call. Captured when the exception
    // is created, while the tail call still sits on the stack; the frame itself is released so the
    // caller's values stay collectable while the native callee is suspended.
    Prototype? tailCallSitePrototype;
    int tailCallSitePc;

    void CaptureTailCallSite(LuaState state)
    {
        var caller = state.GetNativeTailCaller();
        if (
            caller.Prototype is not { } prototype
            || (uint)caller.Pc >= (uint)prototype.LineInfo.Length
        )
            return;
        tailCallSitePrototype = prototype;
        tailCallSitePc = caller.Pc;
    }

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
        // Ordering follows luaG_ordererror: "compare X with Y", not the operator's own name.
        if (op is "lt" or "le" or "compare")
        {
            ThrowComparisonFailed(state, a, b);
        }

        var typeA = a.TypeToString();
        var typeB = b.TypeToString();
        if (typeA == typeB)
        {
            throw new LuaRuntimeException(state, $"attempt to {op} two {typeA} values");
        }

        throw new LuaRuntimeException(
            state,
            $"attempt to {op} a {typeA} value with a {typeB} value"
        );
    }

    static void ThrowComparisonFailed(LuaState? state, LuaValue a, LuaValue b)
    {
        var typeA = LuaValue.ToString(a.Type);
        var typeB = LuaValue.ToString(b.Type);
        if (a.Type == b.Type)
        {
            throw new LuaRuntimeException(state, $"attempt to compare two {typeA} values");
        }

        throw new LuaRuntimeException(state, $"attempt to compare {typeA} with {typeB}");
    }

    public static void AttemptInvalidOperation(LuaState? state, string op, LuaValue a)
    {
        throw new LuaRuntimeException(state, $"attempt to {op} a {a.TypeToString()} value");
    }

    internal static void AttemptInvalidOperationOnLuaStack(
        LuaState state,
        string op,
        int lastPc,
        int regA,
        int regB
    )
    {
        var caller = state.GetCurrentFrame();
        var luaValueA =
            regA < 255
                ? state.Stack[caller.Base + regA]
                : ((LuaClosure)caller.Function).Proto.Constants[regA - 256];
        var luaValueB =
            regB < 255
                ? state.Stack[caller.Base + regB]
                : ((LuaClosure)caller.Function).Proto.Constants[regB - 256];
        var function = caller.Function;
        if (op is "compare")
        {
            // luaG_ordererror describes the values; it never names the operator or the locals.
            ThrowComparisonFailed(state, luaValueA, luaValueB);
        }

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

    internal static void AttemptInvalidOperationOnLuaStack(
        LuaState state,
        string op,
        int lastPc,
        int reg
    )
    {
        var caller = state.GetCurrentFrame();
        var luaValue =
            reg < 255
                ? state.Stack[caller.Base + reg]
                : ((LuaClosure)caller.Function).Proto.Constants[reg - 256];
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
        var luaValue = closure.UpValues[reg].GetValue();
        var name = upValue.Name;

        throw new LuaRuntimeException(
            state,
            $"attempt to {op} a {luaValue.TypeToString()} value (upvalue '{name}')"
        );
    }

    internal static (string NameWhat, string Name) GetCurrentFunctionName(LuaState state)
    {
        var current = state.GetCurrentFrame();
        var pc = current.CallerInstructionIndex;
        Prototype? callerPrototype;
        if (current.IsTailCall)
        {
            (callerPrototype, pc) =
                current.Function is LuaClosure
                    ? (state.LastCallerPrototype, state.LastPc)
                    : (state.LastNativeTailCaller.Prototype, state.LastNativeTailCaller.Pc);
        }
        else
        {
            var frames = state.GetCallStackFrames();
            if (frames.Length < 2)
                return GlobalOrFunctionName(state, current.Function);
            var caller = frames[^2];
            callerPrototype = (caller.Function as LuaClosure)?.Proto;
        }

        if (callerPrototype is null)
        {
            // The caller is a host function (pcall, a C# callback), so the call site cannot name
            // this function; Lua then searches the globals for it.
            return GlobalOrFunctionName(state, current.Function);
        }

        var nameWhat = LuaDebug.GetFuncName(callerPrototype, pc, out var name) ?? "";
        if (
            string.IsNullOrEmpty(name)
            && FindGlobalFunctionName(state, current.Function) is { } global
        )
            return (nameWhat, global);

        return (nameWhat, name ?? current.Function.Name);
    }

    static (string NameWhat, string Name) GlobalOrFunctionName(
        LuaState state,
        LuaFunction function
    ) =>
        FindGlobalFunctionName(state, function) is { } global
            ? ("global", global)
            : ("function", function.Name);

    /// <summary>
    /// Mirrors Lua's pushglobalfuncname: when a library function is called without a usable call
    /// site name, its registered path (for example <c>string.format</c>) is reported instead.
    /// </summary>
    static string? FindGlobalFunctionName(LuaState state, LuaFunction function)
    {
        foreach (var pair in state.GlobalState.Environment)
        {
            if (pair.Key.Type is not LuaValueType.String)
                continue;
            var key = pair.Key.Read<string>();
            if (key.Length == 0 || key[0] == '_')
                continue;
            if (pair.Value.TryReadFunction(out var value) && ReferenceEquals(value, function))
                return key;
            if (!pair.Value.TryRead<LuaTable>(out var table))
                continue;
            foreach (var inner in table)
            {
                if (inner.Key.Type is not LuaValueType.String)
                    continue;
                if (
                    inner.Value.TryReadFunction(out var nested) && ReferenceEquals(nested, function)
                )
                    return key + "." + inner.Key.Read<string>();
            }
        }

        return null;
    }

    public static void BadArgument(LuaState state, int argumentId)
    {
        BadArgument(state, argumentId, "value expected");
    }

    public static void BadArgument(
        LuaState state,
        int argumentId,
        LuaValueType expected,
        LuaValueType actual
    )
    {
        // The single-message overload owns the surrounding parentheses.
        BadArgument(
            state,
            argumentId,
            $"{LuaValue.ToString(expected)} expected, got {LuaValue.ToString(actual)}"
        );
    }

    public static void BadArgument(
        LuaState state,
        int argumentId,
        LuaValueType[] expected,
        LuaValueType actual
    )
    {
        BadArgument(
            state,
            argumentId,
            $"{string.Join(" or ", expected.Select(LuaValue.ToString))} expected, got {LuaValue.ToString(actual)}"
        );
    }

    public static void BadArgument(LuaState state, int argumentId, string expected, string actual)
    {
        BadArgument(state, argumentId, $"{expected} expected, got {actual}");
    }

    public static void BadArgument(LuaState state, int argumentId, string[] expected, string actual)
    {
        if (expected.Length == 0)
        {
            throw new ArgumentException("Expected array must not be empty", nameof(expected));
        }

        BadArgument(state, argumentId, $"{string.Join(" or ", expected)} expected, got {actual}");
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

    public static void ThrowBadArgumentIfNumberIsNotInteger(
        LuaState state,
        int argumentId,
        double value
    )
    {
        if (!MathEx.IsInteger(value))
        {
            BadArgumentNumberIsNotInteger(state, argumentId);
        }
    }

    string FormatMessage(ReadOnlySpan<CallStackFrame> frames, string message, string prefix = "")
    {
        var pooledList = new PooledList<char>(64);
        pooledList.Clear();
        try
        {
            pooledList.AddRange(prefix);
            if (tailCallSitePrototype is { } prototype)
                Traceback.WritePosition(ref pooledList, prototype, tailCallSitePc);
            else
                Traceback.WriteLastLuaTrace(frames, ref pooledList, level);
            pooledList.AddRange(message);
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
        var state = State ?? luaTraceback?.State;
        if (state != null && ReferenceEquals(state.CurrentException, this))
        {
            state.CurrentException = null;
            state.ExceptionTrace.Clear();
        }
        State = null;
    }

    internal string MinimalMessage(LuaPlatform platform)
    {
        var message = InnerException is { } inner
            ? platform.StringMode == LuaStringMode.Bytes
                ? platform.GetErrorMessage(inner)
                : inner.ToString()
            : ErrorObject.ToString();
        if (level <= 0)
        {
            return message;
        }

        ReadOnlySpan<CallStackFrame> frames = default;
        if (luaTraceback is { } traceback)
        {
            frames = traceback.StackFrames;
        }
        else if (State is { } state)
        {
            frames = state.ExceptionTrace.AsSpan();
            if (frames.IsEmpty)
                frames = state.GetCallStackFrames();
        }
        return frames.IsEmpty && tailCallSitePrototype == null
            ? message
            : FormatMessage(frames, message);
    }

    public override string Message
    {
        get
        {
            if (InnerException != null)
            {
                return InnerException.Message;
            }

            var traceback = LuaTraceback;
            return traceback == null
                ? ErrorObject.ToString()
                : FormatMessage(traceback.StackFrames, ErrorObject.ToString(), "Lua-CSharp: ");
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

public class LuaAssertionException(LuaState? traceback, string message)
    : LuaRuntimeException(traceback, message);

public class LuaModuleNotFoundException(string moduleName)
    : Exception($"module '{moduleName}' not found");

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

    internal LuaCanceledException(
        LuaState state,
        CancellationToken cancellationToken,
        Exception? innerException = null
    )
        : base(
            "The operation was cancelled during execution on Lua.",
            innerException,
            cancellationToken
        )
    {
        if (state.IsRunning)
        {
            state.CurrentException?.BuildOrGet();
            state.ExceptionTrace.Clear();
            state.CurrentException = this;
            State = state;
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
            State = null!;
        }

        return luaTraceback;
    }
}
