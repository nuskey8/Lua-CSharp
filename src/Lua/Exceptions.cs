using Lua.CodeAnalysis;
using Lua.CodeAnalysis.Syntax;
using Lua.Internal;
using Lua.Runtime;
using System.Diagnostics;
using System.Runtime.CompilerServices;

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

public class LuaUnDumpException(string message) : Exception(message);

internal interface ILuaTracebackBuildable
{
    Traceback? BuildOrGet();
}

public class LuaRuntimeException : Exception, ILuaTracebackBuildable
{
    public LuaRuntimeException(LuaThread? thread, Exception innerException) : base(innerException.Message, innerException)
    {
        Thread = thread;
    }

    public LuaRuntimeException(LuaThread? thread, LuaValue errorObject)
    {
        if (thread != null)
        {
            thread.CurrentException?.BuildOrGet();
            thread.ExceptionTrace.Clear();
            thread.CurrentException = this;
        }

        Thread = thread;

        ErrorObject = errorObject;
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

    internal LuaThread? Thread { get; private set; } = default!;
    public LuaValue ErrorObject { get; }

    public static void AttemptInvalidOperation(LuaThread? thread, string op, LuaValue a, LuaValue b)
    {
        throw new LuaRuntimeException(thread, $"attempt to {op} a {a.TypeToString()} value with a {b.TypeToString()} value");
    }

    public static void AttemptInvalidOperation(LuaThread? thread, string op, LuaValue a)
    {
        throw new LuaRuntimeException(thread, $"attempt to {op} a {a.TypeToString()} value");
    }

    internal static void AttemptInvalidOperationOnLuaStack(LuaThread thread, string op, int lastPc, int reg)
    {
        var caller = thread.GetCurrentFrame();
        var luaValue = thread.Stack[caller.Base + reg];
        var function = caller.Function;
        var t = LuaDebug.GetName(((LuaClosure)function).Proto, lastPc, reg, out string? name);
        if (t == null || name == null)
        {
            throw new LuaRuntimeException(thread, $"attempt to {op} a {luaValue.TypeToString()} value");
        }
        else
        {
            throw new LuaRuntimeException(thread, $"attempt to {op} a {luaValue.TypeToString()} value ({t} '{name}')");
        }
    }

    public static void BadArgument(LuaThread? thread, int argumentId, string functionName)
    {
        throw new LuaRuntimeException(thread, $"bad argument #{argumentId} to '{functionName}' (value expected)");
    }

    public static void BadArgument(LuaThread? thread, int argumentId, string functionName, LuaValueType[] expected)
    {
        throw new LuaRuntimeException(thread, $"bad argument #{argumentId} to '{functionName}' ({string.Join(" or ", expected)} expected)");
    }

    public static void BadArgument(LuaThread? thread, int argumentId, string functionName, string expected, string actual)
    {
        throw new LuaRuntimeException(thread, $"bad argument #{argumentId} to '{functionName}' ({expected} expected, got {actual})");
    }

    public static void BadArgument(LuaThread? thread, int argumentId, string functionName, string message)
    {
        throw new LuaRuntimeException(thread, $"bad argument #{argumentId} to '{functionName}' ({message})");
    }

    public static void BadArgumentNumberIsNotInteger(LuaThread? thread, int argumentId, string functionName)
    {
        throw new LuaRuntimeException(thread, $"bad argument #{argumentId} to '{functionName}' (number has no integer representation)");
    }

    public static void ThrowBadArgumentIfNumberIsNotInteger(LuaThread? thread, string functionName, int argumentId, double value)
    {
        if (!MathEx.IsInteger(value))
        {
            BadArgumentNumberIsNotInteger(thread, argumentId, functionName);
        }
    }

    static string CreateMessage(Traceback traceback, LuaValue errorObject)
    {
        var pooledList = new PooledList<char>(64);
        pooledList.Clear();
        try
        {
            pooledList.AddRange("Lua-CSharp: ");
            traceback.WriteLastLuaTrace(ref pooledList);
            pooledList.AddRange(": ");
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
        if (luaTraceback != null) return luaTraceback;
        if (Thread != null)
        {
            var callStack = Thread.ExceptionTrace.AsSpan();
            if (callStack.IsEmpty) return null;
            luaTraceback = new Traceback(Thread.State, callStack);
            Thread.ExceptionTrace.Clear();
            Thread = null;
        }

        return luaTraceback;
    }

    internal void Forget()
    {
        Thread?.ExceptionTrace.Clear();
        Thread = null;
    }

    public override string Message
    {
        get
        {
            if (InnerException != null) return InnerException.Message;
            if (LuaTraceback == null)
            {
                return ErrorObject.ToString();
            }

            return CreateMessage(LuaTraceback, ErrorObject);
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

public class LuaAssertionException(LuaThread? traceback, string message) : LuaRuntimeException(traceback, message);

public class LuaModuleNotFoundException(string moduleName) : Exception($"module '{moduleName}' not found");

public sealed class LuaCancelledException : OperationCanceledException, ILuaTracebackBuildable
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

    internal LuaThread? Thread { get; private set; }

    internal LuaCancelledException(LuaThread thread, CancellationToken cancellationToken, Exception? innerException = null) : base("The operation was cancelled during execution on Lua.", innerException, cancellationToken)
    {
        thread.CurrentException?.BuildOrGet();
        thread.ExceptionTrace.Clear();
        thread.CurrentException = this;
        Thread = thread;
    }


    [MethodImpl(MethodImplOptions.NoInlining)]
    Traceback? ILuaTracebackBuildable.BuildOrGet()
    {
        if (luaTraceback != null) return luaTraceback;

        if (Thread != null)
        {
            var callStack = Thread.ExceptionTrace.AsSpan();
            if (callStack.IsEmpty) return null;
            luaTraceback = new Traceback(Thread.State, callStack);
            Thread.ExceptionTrace.Clear();
            Thread = null!;
        }

        return luaTraceback;
    }
}