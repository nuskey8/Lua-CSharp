using Lua.Internal;
using Lua.Runtime;

namespace Lua;

public class LuaException : Exception
{
    protected LuaException(Exception innerException) : base(innerException.Message, innerException)
    {
    }

    public LuaException(string message) : base(message)
    {
    }

    protected LuaException()
    {
    }
}

public class LuaParseException(string message) : LuaException(message);

public class LuaRuntimeException : LuaException
{
    public LuaRuntimeException(Traceback traceback, Exception innerException) : base(innerException)
    {
        LuaTraceback = traceback;
    }

    public LuaRuntimeException(Traceback traceback, LuaValue errorObject) : base(CreateMessage(traceback, errorObject))
    {
        LuaTraceback = traceback;
        ErrorObject = errorObject;
    }

    public Traceback LuaTraceback { get; }
    public LuaValue ErrorObject { get; }

    public static void AttemptInvalidOperation(Traceback traceback, string op, LuaValue a, LuaValue b)
    {
        throw new LuaRuntimeException(traceback, $"attempt to {op} a '{a.Type}' with a '{b.Type}'");
    }

    public static void AttemptInvalidOperation(Traceback traceback, string op, LuaValue a)
    {
        throw new LuaRuntimeException(traceback, $"attempt to {op} a '{a.Type}' value");
    }

    public static void BadArgument(Traceback traceback, int argumentId, string functionName)
    {
        throw new LuaRuntimeException(traceback, $"bad argument #{argumentId} to '{functionName}' (value expected)");
    }

    public static void BadArgument(Traceback traceback, int argumentId, string functionName, LuaValueType[] expected)
    {
        throw new LuaRuntimeException(traceback, $"bad argument #{argumentId} to '{functionName}' ({string.Join(" or ", expected)} expected)");
    }

    public static void BadArgument(Traceback traceback, int argumentId, string functionName, string expected, string actual)
    {
        throw new LuaRuntimeException(traceback, $"bad argument #{argumentId} to '{functionName}' ({expected} expected, got {actual})");
    }

    public static void BadArgument(Traceback traceback, int argumentId, string functionName, string message)
    {
        throw new LuaRuntimeException(traceback, $"bad argument #{argumentId} to '{functionName}' ({message})");
    }

    public static void BadArgumentNumberIsNotInteger(Traceback traceback, int argumentId, string functionName)
    {
        throw new LuaRuntimeException(traceback, $"bad argument #{argumentId} to '{functionName}' (number has no integer representation)");
    }

    public static void ThrowBadArgumentIfNumberIsNotInteger(LuaThread thread, string functionName, int argumentId, double value)
    {
        if (!MathEx.IsInteger(value))
        {
            BadArgumentNumberIsNotInteger(thread.GetTraceback(), argumentId, functionName);
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


    public override string ToString()
    {
        var pooledList = new PooledList<char>(64);
        pooledList.Clear();
        try
        {
            pooledList.AddRange(base.Message);
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

public class LuaAssertionException(Traceback traceback, string message) : LuaRuntimeException(traceback, message)
{
    // public override string ToString()
    // {
    //     return $"{Message}\n{StackTrace}";
    // }
}

public class LuaModuleNotFoundException(string moduleName) : LuaException($"module '{moduleName}' not found");