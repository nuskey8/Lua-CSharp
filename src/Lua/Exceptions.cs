using Lua.CodeAnalysis;
using Lua.CodeAnalysis.Syntax;
using Lua.Internal;
using Lua.Runtime;

namespace Lua;

public class LuaException : Exception
{
    protected LuaException(Exception innerException) : base(innerException.Message, innerException)
    {
    }

    protected LuaException(string message, Exception innerException) : base(message, innerException)
    {
    }

    public LuaException(string message) : base(message)
    {
    }

    protected LuaException()
    {
    }
}

public class LuaParseException(string? chunkName, SourcePosition position, string message) : LuaException(message)
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

public class LuaCompileException(string chunkName, SourcePosition position, int offset, string message, string? nearToken) : LuaException(GetMessageWithNearToken(message, nearToken))
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

public class LuaUnDumpException(string message) : LuaException(message);

public class LuaRuntimeException : LuaException
{
    public LuaRuntimeException(LuaThread? thread, Exception innerException) : base(innerException)
    {
        Thread = thread;
    }

    public LuaRuntimeException(LuaThread? thread, LuaValue errorObject)
    {
        Thread = thread;
        ErrorObject = errorObject;
    }

    public Traceback? LuaTraceback { get; private set; }
    internal LuaThread? Thread { get; private set; } = default!;
    public LuaValue ErrorObject { get; }

    public static void AttemptInvalidOperation(LuaThread? traceback, string op, LuaValue a, LuaValue b)
    {
        throw new LuaRuntimeException(traceback, $"attempt to {op} a '{a.Type}' with a '{b.Type}'");
    }

    public static void AttemptInvalidOperation(LuaThread? traceback, string op, LuaValue a)
    {
        throw new LuaRuntimeException(traceback, $"attempt to {op} a '{a.Type}' value");
    }

    public static void BadArgument(LuaThread? traceback, int argumentId, string functionName)
    {
        throw new LuaRuntimeException(traceback, $"bad argument #{argumentId} to '{functionName}' (value expected)");
    }

    public static void BadArgument(LuaThread? traceback, int argumentId, string functionName, LuaValueType[] expected)
    {
        throw new LuaRuntimeException(traceback, $"bad argument #{argumentId} to '{functionName}' ({string.Join(" or ", expected)} expected)");
    }

    public static void BadArgument(LuaThread? traceback, int argumentId, string functionName, string expected, string actual)
    {
        throw new LuaRuntimeException(traceback, $"bad argument #{argumentId} to '{functionName}' ({expected} expected, got {actual})");
    }

    public static void BadArgument(LuaThread? traceback, int argumentId, string functionName, string message)
    {
        throw new LuaRuntimeException(traceback, $"bad argument #{argumentId} to '{functionName}' ({message})");
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

    internal void BuildWithPop(int top)
    {
        if (Thread != null)
        {
            var callStack = Thread.CallStack.AsSpan()[top..];
            if (callStack.IsEmpty) return;
            LuaTraceback = new Traceback(Thread.State) { RootFunc = callStack[0].Function, StackFrames = callStack[1..].ToArray(), };
            Thread.CallStack.PopUntil(top);
        }
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
            return base.ToString();
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
        //return $"{Message} {StackTrace}";
    }
}

public class LuaAssertionException(LuaThread? traceback, string message) : LuaRuntimeException(traceback, message)
{
    // public override string ToString()
    // {
    //     return $"{Message}\n{StackTrace}";
    // }
}

public class LuaModuleNotFoundException(string moduleName) : LuaException($"module '{moduleName}' not found");