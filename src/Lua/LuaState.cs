using Lua.CodeAnalysis.Compilation;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Lua.Internal;
using Lua.IO;
using Lua.Loaders;
using Lua.Platforms;
using Lua.Runtime;
using Lua.Standard;
using System.Buffers;

namespace Lua;

public sealed class LuaState
{
    // states
    readonly LuaMainThread mainThread;
    FastListCore<UpValue> openUpValues;
    FastStackCore<LuaThread> threadStack;
    readonly LuaTable environment;
    readonly LuaTable registry = new();
    readonly UpValue envUpValue;


    FastStackCore<LuaDebug.LuaDebugBuffer> debugBufferPool;

    internal int CallCount;
    internal UpValue EnvUpValue => envUpValue;
    internal ref FastStackCore<LuaThread> ThreadStack => ref threadStack;
    internal ref FastListCore<UpValue> OpenUpValues => ref openUpValues;
    internal ref FastStackCore<LuaDebug.LuaDebugBuffer> DebugBufferPool => ref debugBufferPool;

    public LuaTable Environment => environment;
    public LuaTable Registry => registry;
    public LuaTable LoadedModules => registry[ModuleLibrary.LoadedKeyForRegistry].Read<LuaTable>();
    public LuaTable PreloadModules => registry[ModuleLibrary.PreloadKeyForRegistry].Read<LuaTable>();
    public LuaMainThread MainThread => mainThread;

    public LuaThreadAccess RootAccess => new(mainThread, 0);

    public LuaPlatform Platform { get; }

    public ILuaModuleLoader ModuleLoader { get; set; } = FileModuleLoader.Instance;

    public ILuaFileSystem FileSystem => Platform.FileSystem ?? throw new InvalidOperationException("FileSystem is not set. Please set it before access.");

    public ILuaOsEnvironment OsEnvironment => Platform.OsEnvironment ?? throw new InvalidOperationException("OperatingSystem is not set. Please set it before access.");

    public ILuaStandardIO StandardIO => Platform.StandardIO;

    // metatables
    LuaTable? nilMetatable;
    LuaTable? numberMetatable;
    LuaTable? stringMetatable;
    LuaTable? booleanMetatable;
    LuaTable? functionMetatable;
    LuaTable? threadMetatable;

    public static LuaState Create(LuaPlatform? platform = null)
    {
        var state = new LuaState(platform ?? LuaPlatform.Default);
        return state;
    }

    LuaState(LuaPlatform platform)
    {
        mainThread = new(this);
        environment = new();
        envUpValue = UpValue.Closed(environment);
        registry[ModuleLibrary.LoadedKeyForRegistry] = new LuaTable(0, 8);
        registry[ModuleLibrary.PreloadKeyForRegistry] = new LuaTable(0, 8);
        Platform = platform;
    }


    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal bool TryGetMetatable(LuaValue value, [NotNullWhen(true)] out LuaTable? result)
    {
        result = value.Type switch
        {
            LuaValueType.Nil => nilMetatable,
            LuaValueType.Boolean => booleanMetatable,
            LuaValueType.String => stringMetatable,
            LuaValueType.Number => numberMetatable,
            LuaValueType.Function => functionMetatable,
            LuaValueType.Thread => threadMetatable,
            LuaValueType.UserData => value.UnsafeRead<ILuaUserData>().Metatable,
            LuaValueType.Table => value.UnsafeRead<LuaTable>().Metatable,
            _ => null
        };

        return result != null;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal void SetMetatable(LuaValue value, LuaTable metatable)
    {
        switch (value.Type)
        {
            case LuaValueType.Nil:
                nilMetatable = metatable;
                break;
            case LuaValueType.Boolean:
                booleanMetatable = metatable;
                break;
            case LuaValueType.String:
                stringMetatable = metatable;
                break;
            case LuaValueType.Number:
                numberMetatable = metatable;
                break;
            case LuaValueType.Function:
                functionMetatable = metatable;
                break;
            case LuaValueType.Thread:
                threadMetatable = metatable;
                break;
            case LuaValueType.UserData:
                value.UnsafeRead<ILuaUserData>().Metatable = metatable;
                break;
            case LuaValueType.Table:
                value.UnsafeRead<LuaTable>().Metatable = metatable;
                break;
        }
    }

    internal UpValue GetOrAddUpValue(LuaThread thread, int registerIndex)
    {
        foreach (var upValue in openUpValues.AsSpan())
        {
            if (upValue.RegisterIndex == registerIndex && upValue.Thread == thread)
            {
                return upValue;
            }
        }

        var newUpValue = UpValue.Open(thread, registerIndex);
        openUpValues.Add(newUpValue);
        return newUpValue;
    }

    internal void CloseUpValues(LuaThread thread, int frameBase)
    {
        for (int i = 0; i < openUpValues.Length; i++)
        {
            var upValue = openUpValues[i];
            if (upValue.Thread != thread) continue;

            if (upValue.RegisterIndex >= frameBase)
            {
                upValue.Close();
                openUpValues.RemoveAtSwapBack(i);
                i--;
            }
        }
    }

    public unsafe LuaClosure Load(ReadOnlySpan<char> chunk, string chunkName, LuaTable? environment = null)
    {
        Prototype prototype;
        fixed (char* ptr = chunk)
        {
            prototype = Parser.Parse(this, new(ptr, chunk.Length), chunkName);
        }

        return new LuaClosure(MainThread, prototype, environment);
    }

    public LuaClosure Load(ReadOnlySpan<byte> chunk, string? chunkName = null, string mode = "bt", LuaTable? environment = null)
    {
        if (chunk.Length > 4)
        {
            if (chunk[0] == '\e')
            {
                return new LuaClosure(MainThread, Parser.UnDump(chunk, chunkName), environment);
            }
        }

        chunk = BomUtility.GetEncodingFromBytes(chunk, out var encoding);

        var charCount = encoding.GetCharCount(chunk);
        var pooled = ArrayPool<char>.Shared.Rent(charCount);
        try
        {
            var chars = pooled.AsSpan(0, charCount);
            encoding.GetChars(chunk, chars);
            chunkName ??= chars.ToString();

            return Load(chars, chunkName, environment);
        }
        finally
        {
            ArrayPool<char>.Shared.Return(pooled);
        }
    }
}