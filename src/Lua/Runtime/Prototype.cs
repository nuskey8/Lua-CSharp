using Lua.CodeAnalysis;

namespace Lua.Runtime;

public sealed class Prototype(
    string chunkName,
    int lineDefined,
    int lastLineDefined,
    int parameterCount,
    int maxStackSize,
    bool hasVariableArguments,
    LuaValue[] constants,
    Instruction[] code,
    Prototype[] childPrototypes,
    int[] lineInfo,
    LocalVariable[] localVariables,
    UpValueDesc[] upValues
)
{
    public ReadOnlySpan<LuaValue> Constants => constants;
    public ReadOnlySpan<Instruction> Code => code;
    public ReadOnlySpan<Prototype> ChildPrototypes => childPrototypes;
    public ReadOnlySpan<int> LineInfo => lineInfo;
    public ReadOnlySpan<LocalVariable> LocalVariables => localVariables;
    public ReadOnlySpan<UpValueDesc> UpValues => upValues;

    //public LuaClosure Cache;
    public readonly string ChunkName = chunkName;
    public readonly int LineDefined = lineDefined, LastLineDefined = lastLineDefined;
    public readonly int ParameterCount = parameterCount, MaxStackSize = maxStackSize;
    public readonly bool HasVariableArguments = hasVariableArguments;
}