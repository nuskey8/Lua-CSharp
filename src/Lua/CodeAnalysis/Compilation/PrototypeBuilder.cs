using Lua.Internal;
using System;
using Lua.Runtime;

namespace Lua.CodeAnalysis.Compilation;

internal class PrototypeBuilder : IPoolNode<PrototypeBuilder>
{
    internal FastListCore<LuaValue> ConstantsList;
    public ReadOnlySpan<LuaValue> Constants => ConstantsList.AsSpan();
    internal FastListCore<Instruction> CodeList;
    public ReadOnlySpan<Instruction> Code => CodeList.AsSpan();
    internal FastListCore<PrototypeBuilder> PrototypeList;
    public ReadOnlySpan<PrototypeBuilder> Prototypes => PrototypeList.AsSpan();
    internal FastListCore<int> LineInfoList;
    public ReadOnlySpan<int> LineInfo => LineInfoList.AsSpan();
    internal FastListCore<LocalVariable> LocalVariablesList;
    public ReadOnlySpan<LocalVariable> LocalVariables => LocalVariablesList.AsSpan();

    internal FastListCore<UpValueDesc> UpValuesList;

    public ReadOnlySpan<UpValueDesc> UpValues => UpValuesList.AsSpan();

    //public LuaClosure Cache;
    public string Source;
    public int LineDefined, LastLineDefined;
    public int ParameterCount, MaxStackSize;
    public bool IsVarArg;


    internal PrototypeBuilder(string source)
    {
        Source = source;
    }

    static LinkedPool<PrototypeBuilder> pool;


    PrototypeBuilder? nextNode;
    ref PrototypeBuilder? IPoolNode<PrototypeBuilder>.NextNode => ref nextNode;

    internal static PrototypeBuilder Get(string source)
    {
        if (!pool.TryPop(out var f))
        {
            f = new PrototypeBuilder(source);
        }

        f.Source = source;
        return f;
    }

    internal void Release()
    {
        ConstantsList.Clear();
        CodeList.Clear();
        PrototypeList.Clear();
        LineInfoList.Clear();
        LocalVariablesList.Clear();
        UpValuesList.Clear();
        pool.TryPush(this);
    }


    public Prototype CreatePrototypeAndRelease()
    {
        var protoTypes = Prototypes.Length == 0 ? Array.Empty<Prototype>() : new Prototype[Prototypes.Length];
        for (var i = 0; i < Prototypes.Length; i++)
        {
            protoTypes[i] = Prototypes[i].CreatePrototypeAndRelease(); //ref
        }

        var p = new Prototype(Source, LineDefined, LastLineDefined, ParameterCount, MaxStackSize, IsVarArg, Constants.ToArray(), Code.ToArray(), protoTypes, LineInfo.ToArray(), LocalVariables.ToArray(), UpValues.ToArray());
        Release();
        return p;
    }
}