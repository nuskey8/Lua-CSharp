namespace Lua.Runtime;

public static class Metamethods
{
    public const string Metatable = "__metatable";
    public const string Index = "__index";
    public const string NewIndex = "__newindex";
    public const string Add = "__add";
    public const string Sub = "__sub";
    public const string Mul = "__mul";
    public const string Div = "__div";
    public const string Mod = "__mod";
    public const string Pow = "__pow";
    public const string Unm = "__unm";
    public const string Len = "__len";
    public const string Eq = "__eq";
    public const string Lt = "__lt";
    public const string Le = "__le";
    public const string Call = "__call";
    public const string Concat = "__concat";
    public const string Pairs = "__pairs";
    public const string IPairs = "__ipairs";
    public new const string ToString = "__tostring";

    internal static (string Name, string Description) GetNameAndDescription(this OpCode opCode)
    {
        return opCode switch
        {
            OpCode.GetTabUp or OpCode.GetTable or OpCode.Self => (Index, "index"),
            OpCode.SetTabUp or OpCode.SetTable => (NewIndex, "new index"),
            OpCode.Add => (Add, "add"),
            OpCode.Sub => (Sub, "sub"),
            OpCode.Mul => (Mul, "mul"),
            OpCode.Div => (Div, "div"),
            OpCode.Mod => (Mod, "mod"),
            OpCode.Pow => (Pow, "pow"),
            OpCode.Unm => (Unm, "unm"),
            OpCode.Len => (Len, "get length of"),
            OpCode.Eq => (Eq, "eq"),
            OpCode.Lt => (Lt, "lt"),
            OpCode.Le => (Le, "le"),
            OpCode.Call => (Call, "call"),
            OpCode.Concat => (Concat, "concatenate"),
            _ => (opCode.ToString(), opCode.ToString()),
        };
    }
}