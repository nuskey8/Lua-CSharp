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
using System.Text;

namespace Lua;

public sealed class LuaGlobalState
{
    // states
    readonly LuaState mainState;
    FastStackCore<LuaState> stateStack;
    readonly LuaTable environment;
    readonly LuaTable registry = new();
    readonly UpValue envUpValue;

    FastStackCore<LuaDebug.LuaDebugBuffer> debugBufferPool;

    internal UpValue EnvUpValue => envUpValue;

    internal ref FastStackCore<LuaState> ThreadStack => ref stateStack;

    internal ref FastStackCore<LuaDebug.LuaDebugBuffer> DebugBufferPool => ref debugBufferPool;

    public LuaTable Environment => environment;

    public LuaTable Registry => registry;

    public LuaTable LoadedModules => registry[ModuleLibrary.LoadedKeyForRegistry].Read<LuaTable>();

    public LuaTable PreloadModules => registry[ModuleLibrary.PreloadKeyForRegistry].Read<LuaTable>();

    public LuaState MainThread => mainState;

    public LuaPlatform Platform { get; set; }

    public ILuaModuleLoader? ModuleLoader { get; set; }

    // metatables
    LuaTable? nilMetatable;
    LuaTable? numberMetatable;
    LuaTable? stringMetatable;
    LuaTable? booleanMetatable;
    LuaTable? functionMetatable;
    LuaTable? stateMetatable;

    public static LuaGlobalState Create(LuaPlatform? platform = null)
    {
        LuaGlobalState globalState = new(platform ?? LuaPlatform.Default);
        return globalState;
    }

    LuaGlobalState(LuaPlatform platform)
    {
        mainState = new(this);
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
            LuaValueType.Thread => stateMetatable,
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
                stateMetatable = metatable;
                break;
            case LuaValueType.UserData:
                value.UnsafeRead<ILuaUserData>().Metatable = metatable;
                break;
            case LuaValueType.Table:
                value.UnsafeRead<LuaTable>().Metatable = metatable;
                break;
        }
    }
}