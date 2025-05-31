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
        switch (opCode)
        {
            case OpCode.GetTabUp:
            case OpCode.GetTable:
            case OpCode.Self:
                return (Index, "index");
            case OpCode.SetTabUp:
            case OpCode.SetTable:
                return (NewIndex, "new index");
            case OpCode.Add:
                return (Add, "add");
            case OpCode.Sub:
                return (Sub, "sub");
            case OpCode.Mul:
                return (Mul, "mul");
            case OpCode.Div:
                return (Div, "div");
            case OpCode.Mod:
                return (Mod, "mod");
            case OpCode.Pow:
                return (Pow, "pow");
            case OpCode.Unm:
                return (Unm, "unm");
            case OpCode.Len:
                return (Len, "get length of");
            case OpCode.Eq:
                return (Eq, "eq");
            case OpCode.Lt:
                return (Lt, "lt");
            case OpCode.Le:
                return (Le, "le");
            case OpCode.Call:
                return (Call, "call");
            case OpCode.Concat:
                return (Concat, "concatenate");
            default: return (opCode.ToString(), opCode.ToString());
        }
    }
}