using Lua.Internal;
using Lua.Runtime;
using System.Buffers;
using System.Runtime.CompilerServices;
using static System.Diagnostics.Debug;
namespace Lua.CodeAnalysis.Compilation;

using static Function;
using static Scanner;
using static Constants;

internal class Parser : IPoolNode<Parser>, IDisposable
{
    /// inline
    internal Scanner Scanner;

    internal int T => Scanner.Token.T;
    internal bool TestNext(int token) => Scanner.TestNext(token);
    internal void Next() => Scanner.Next();

    internal Function Function = null!;
    internal FastListCore<int> ActiveVariables;
    internal FastListCore<Label> PendingGotos;
    internal FastListCore<Label> ActiveLabels;

    Parser()
    {
    }

    Parser? nextNode;
    ref Parser? IPoolNode<Parser>.NextNode => ref nextNode;

    static LinkedPool<Parser> pool;

    static Parser Get(Scanner scanner)
    {
        if (!pool.TryPop(out var parser))
        {
            parser = new Parser();
        }

        parser.Scanner = scanner;
        return parser;
    }

    void IDisposable.Dispose() => Release();

    public void Release()
    {
        ActiveVariables.Clear();
        PendingGotos.Clear();
        ActiveLabels.Clear();
        pool.TryPush(this);
    }


    public void CheckCondition(bool c, string message)
    {
        if (!c)
        {
            Scanner.SyntaxError(message);
        }
    }


    public string CheckName()
    {
        Scanner.Check(TkName);
        var s = Scanner.Token.S;
        Next();
        return s;
    }


    public void CheckLimit(int val, int limit, string what)
    {
        if (val > limit)
        {
            string where = "main function";
            var line = Function.Proto.LineDefined;
            if (line != 0)
            {
                where = $"function at line {line}";
            }

            Scanner.SyntaxError($"too many {what} (limit is {limit}) in {where}");
        }
    }


    public void CheckNext(int t)
    {
        Scanner.Check(t);
        Next();
    }


    public ExprDesc CheckNameAsExpression() => Function.EncodeString(CheckName());


    public ExprDesc SingleVariable() => Function.SingleVariable(CheckName());


    public void LeaveLevel() => Scanner.L.CallCount--;


    public TempBlock EnterLevel()
    {
        Scanner.L.CallCount++;
        CheckLimit(Scanner.L.CallCount, MaxCallCount, "Go levels");
        return new TempBlock(Scanner.L);
    }


    public (ExprDesc e, int n) ExpressionList()
    {
        var n = 1;
        var e = Expression();
        for (; TestNext(','); n++, e = Expression())
        {
            Function.ExpressionToNextRegister(e);
        }

        return (e, n);
    }


    public (int, int, int, ExprDesc) Field(int tableRegister, int a, int h, int pending, ExprDesc e)
    {
        var freeRegisterCount = Function.FreeRegisterCount;

        void hashField(ExprDesc k)
        {
            h++;
            CheckNext('=');
            Function.FlushFieldToConstructor(tableRegister, freeRegisterCount, k, Expression);
        }

        if (T == TkName && Scanner.LookAhead() == '=')
        {
            CheckLimit(h, MaxInt, "items in a constructor");
            hashField(CheckNameAsExpression());
        }
        else if (T == '[')
        {
            hashField(Index());
        }
        else
        {
            e = Expression();
            CheckLimit(a, MaxInt, "items in a constructor");
            a++;
            pending++;
        }

        return (a, h, pending, e);
    }


    public ExprDesc Constructor()
    {
        var (pc, t) = Function.OpenConstructor();
        var (line, a, h, pending) = (Scanner.LineNumber, 0, 0, 0);
        ExprDesc e = default;
        CheckNext('{');
        if (T != '}')
        {
            (a, h, pending, e) = Field(t.Info, a, h, pending, e);
            while ((TestNext(',') || TestNext(';')) && T != '}')
            {
                if (e.Kind != Kind.Void)
                {
                    pending = Function.FlushToConstructor(t.Info, pending, a, e);
                    e.Kind = Kind.Void;
                }

                (a, h, pending, e) = Field(t.Info, a, h, pending, e);
            }
        }

        Scanner.CheckMatch('}', '{', line);
        Function.CloseConstructor(pc, t.Info, pending, a, h, e);
        return t;
    }


    public ExprDesc FunctionArguments(ExprDesc f, int line)
    {
        ExprDesc args = default;
        switch (T)
        {
            case '(':
                Next();
                if (T == ')')
                {
                    args.Kind = Kind.Void;
                }
                else
                {
                    (args, _) = ExpressionList();
                    Function.SetMultipleReturns(args);
                }

                Scanner.CheckMatch(')', '(', line);
                break;
            case '{':
                args = Constructor();
                break;
            case TkString:
                args = Function.EncodeString(Scanner.Token.S);
                Next();
                break;
            default:
                Scanner.SyntaxError("function arguments expected");
                break;
        }

        var (@base, parameterCount) = (f.Info, MultipleReturns);
        if (!args.HasMultipleReturns())
        {
            if (args.Kind != Kind.Void)
            {
                Function.ExpressionToNextRegister(args);
            }

            parameterCount = Function.FreeRegisterCount - (@base + 1);
        }

        var e = MakeExpression(Kind.Call, Function.EncodeABC(OpCode.Call, @base, parameterCount + 1, 2));
        Function.FixLine(line);
        Function.FreeRegisterCount = @base + 1; // call removed function and args & leaves (unless changed) one result
        return e;
    }


    public ExprDesc PrimaryExpression()
    {
        ExprDesc e;
        switch (T)
        {
            case '(':
                var line = Scanner.LineNumber;
                Next();
                e = Expression();
                Scanner.CheckMatch(')', '(', line);
                e = Function.DischargeVariables(e);
                return e;
            case TkName:
                return SingleVariable();
            default:
                Scanner.SyntaxError("unexpected symbol");
                return default;
        }
    }


    public ExprDesc SuffixedExpression()
    {
        var line = Scanner.LineNumber;
        var e = PrimaryExpression();
        while (true)
        {
            switch (T)
            {
                case '.':
                    e = FieldSelector(e);
                    break;
                case '[':
                    e = Function.Indexed(Function.ExpressionToAnyRegisterOrUpValue(e), Index());
                    break;
                case ':':
                    Next();
                    e = FunctionArguments(Function.Self(e, CheckNameAsExpression()), line);
                    break;
                case '(':
                case TkString:
                case '{':
                    e = FunctionArguments(Function.ExpressionToNextRegister(e), line);
                    break;
                default:
                    return e;
            }
        }
    }


    public ExprDesc SimpleExpression()
    {
        ExprDesc e;
        switch (T)
        {
            case TkNumber:
                e = MakeExpression(Kind.Number, 0);
                e.Value = Scanner.Token.N;
                break;
            case TkString:
                e = Function.EncodeString(Scanner.Token.S);
                break;
            case TkNil:
                e = MakeExpression(Kind.Nil, 0);
                break;
            case TkTrue:
                e = MakeExpression(Kind.True, 0);
                break;
            case TkFalse:
                e = MakeExpression(Kind.False, 0);
                break;
            case TkDots:
                CheckCondition(Function.Proto.IsVarArg, "cannot use '...' outside a vararg function");
                e = MakeExpression(Kind.VarArg, Function.EncodeABC(OpCode.VarArg, 0, 1, 0));
                break;
            case '{':
                e = Constructor();
                return e;
            case TkFunction:
                Next();
                e = Body(false, Scanner.LineNumber);
                return e;
            default:
                e = SuffixedExpression();
                return e;
        }

        Next();
        return e;
    }


    public static int UnaryOp(int op)
    {
        switch (op)
        {
            case TkNot:
                return OprNot;
            case '-':
                return OprMinus;
            case '#':
                return OprLength;
        }

        return OprNoUnary;
    }


    public static int BinaryOp(int op)
    {
        switch (op)
        {
            case '+':
                return OprAdd;
            case '-':
                return OprSub;
            case '*':
                return OprMul;
            case '/':
                return OprDiv;
            case '%':
                return OprMod;
            case '^':
                return OprPow;
            case TkConcat:
                return OprConcat;
            case TkNe:
                return OprNE;
            case TkEq:
                return OprEq;
            case '<':
                return OprLT;
            case TkLe:
                return OprLE;
            case '>':
                return OprGT;
            case TkGe:
                return OprGE;
            case TkAnd:
                return OprAnd;
            case TkOr:
                return OprOr;
        }

        return OprNoBinary;
    }


    static readonly (int Left, int Right)[] priority =
    [
        (6, 6), (6, 6), (7, 7), (7, 7), (7, 7),
        (10, 9), (5, 4),
        (3, 3), (3, 3), (3, 3),
        (3, 3), (3, 3), (3, 3),
        (2, 2), (1, 1)
    ];

    public static int UnaryPriority => 8;


    public (ExprDesc, int ) SubExpression(int limit)
    {
        using var b = EnterLevel();
        ExprDesc e;
        int u = UnaryOp(T);
        if (u != OprNoUnary)
        {
            int line = Scanner.LineNumber;
            Next();
            (e, _) = SubExpression(UnaryPriority);
            e = Function.Prefix(u, e, line);
        }
        else
        {
            e = SimpleExpression();
        }

        int op = BinaryOp(T);
        while (op != OprNoBinary && priority[op].Left > limit)
        {
            int line = Scanner.LineNumber;
            Next();
            e = Function.Infix(op, e);
            (ExprDesc e2, int next) = SubExpression(priority[op].Right);
            e = Function.Postfix(op, e, e2, line);
            op = next;
        }

        return (e, op);
    }


    public ExprDesc Expression()
    {
        (ExprDesc e, _) = SubExpression(0);
        return e;
    }


    public bool BlockFollow(bool withUntil)
    {
        switch (T)
        {
            case TkElse:
            case TkElseif:
            case TkEnd:
            case TkEos:
                return true;
            case TkUntil:
                return withUntil;
        }

        return false;
    }


    public void StatementList()
    {
        while (!BlockFollow(true))
        {
            if (T == TkReturn)
            {
                Statement();
                return;
            }

            Statement();
        }
    }


    public ExprDesc FieldSelector(ExprDesc e)
    {
        e = Function.ExpressionToAnyRegisterOrUpValue(e);
        Next(); // skip dot or colon
        return Function.Indexed(e, CheckNameAsExpression());
    }


    public ExprDesc Index()
    {
        Next(); // skip '['
        ExprDesc e = Function.ExpressionToValue(Expression());
        CheckNext(']');
        return e;
    }


    public void Assignment(AssignmentTarget t, int variableCount)
    {
        CheckCondition(t.Description.IsVariable(), "syntax error");
        if (TestNext(','))
        {
            ExprDesc e = SuffixedExpression();
            if (e.Kind != Kind.Indexed)
            {
                Function.CheckConflict(t, e);
            }

            CheckLimit(variableCount + Scanner.L.CallCount, MaxCallCount, "Go levels");
            Assignment(new(previous: ref t, exprDesc: e), variableCount + 1);
        }
        else
        {
            CheckNext('=');
            var (e, n) = ExpressionList();
            if (n != variableCount)
            {
                Function.AdjustAssignment(variableCount, n, e);
                if (n > variableCount)
                {
                    Function.FreeRegisterCount -= n - variableCount; // remove extra values
                }
            }
            else
            {
                Function.StoreVariable(t.Description, Function.SetReturn(e));
                return; // avoid default
            }
        }

        Function.StoreVariable(t.Description, MakeExpression(Kind.NonRelocatable, Function.FreeRegisterCount - 1));
        //t.Release();
    }


    public void ForBody(int @base, int line, int n, bool isNumeric)
    {
        Function.AdjustLocalVariables(3);
        CheckNext(TkDo);
        var prep = Function.OpenForBody(@base, n, isNumeric);
        Block();
        Function.CloseForBody(prep, @base, line, n, isNumeric);
    }


    public void ForNumeric(string name, int line)
    {
        void expr()
        {
            ExprDesc e = Function.ExpressionToNextRegister(Expression());
            Assert(e.Kind == Kind.NonRelocatable);
        }

        var @base = Function.FreeRegisterCount;
        Function.MakeLocalVariable("(for index)");
        Function.MakeLocalVariable("(for limit)");
        Function.MakeLocalVariable("(for step)");
        Function.MakeLocalVariable(name);
        CheckNext('=');
        expr();
        CheckNext(',');
        expr();
        if (TestNext(','))
        {
            expr();
        }
        else
        {
            Function.EncodeConstant(Function.FreeRegisterCount, Function.NumberConstant(1));
            Function.ReserveRegisters(1);
        }

        ForBody(@base, line, 1, true);
    }


    public void ForList(string name)
    {
        var n = 4;
        var @base = Function.FreeRegisterCount;
        Function.MakeLocalVariable("(for generator)");
        Function.MakeLocalVariable("(for state)");
        Function.MakeLocalVariable("(for control)");
        Function.MakeLocalVariable(name);
        while (TestNext(','))
        {
            Function.MakeLocalVariable(CheckName());
            n++;
        }

        CheckNext(TkIn);
        var line = Scanner.LineNumber;
        var (e, c) = ExpressionList();
        Function.AdjustAssignment(3, c, e);
        Function.CheckStack(3);
        ForBody(@base, line, n - 3, false);
    }


    public void ForStatement(int line)
    {
        Function.EnterBlock(true);
        Next();
        var name = CheckName();
        switch (T)
        {
            case '=':
                ForNumeric(name, line);
                break;
            case ',':
            case TkIn:
                ForList(name);
                break;
            default:
                Scanner.SyntaxError("'=' or 'in' expected");
                break;
        }

        Scanner.CheckMatch(TkEnd, TkFor, line);
        Function.LeaveBlock();
    }


    public int TestThenBlock(int escapes)
    {
        int jumpFalse;
        Next();
        var e = Expression();
        CheckNext(TkThen);
        if (T == TkGoto || T == TkBreak)
        {
            e = Function.GoIfFalse(e);
            Function.EnterBlock(false);
            GotoStatement(e.T);
            SkipEmptyStatements();
            if (BlockFollow(false))
            {
                Function.LeaveBlock();
                return escapes;
            }

            jumpFalse = Function.Jump();
        }
        else
        {
            e = Function.GoIfTrue(e);
            Function.EnterBlock(false);
            jumpFalse = e.F;
        }

        StatementList();
        Function.LeaveBlock();
        if (T is TkElse or TkElseif)
        {
            escapes = Function.Concatenate(escapes, Function.Jump());
        }

        Function.PatchToHere(jumpFalse);
        return escapes;
    }


    public void IfStatement(int line)
    {
        var escapes = TestThenBlock(NoJump);
        while (T == TkElseif)
        {
            escapes = TestThenBlock(escapes);
        }

        if (TestNext(TkElse))
        {
            Block();
        }

        Scanner.CheckMatch(TkEnd, TkIf, line);
        Function.PatchToHere(escapes);
    }


    public void Block()
    {
        Function.EnterBlock(false);
        StatementList();
        Function.LeaveBlock();
    }


    public void WhileStatement(int line)
    {
        Next();
        var top = Function.Label();
        var conditionExit = Condition();
        Function.EnterBlock(true);
        CheckNext(TkDo);
        Block();
        Function.JumpTo(top);
        Scanner.CheckMatch(TkEnd, TkWhile, line);
        Function.LeaveBlock();
        Function.PatchToHere(conditionExit);
    }


    public void RepeatStatement(int line)
    {
        var top = Function.Label();
        Function.EnterBlock(true); // loop block
        Function.EnterBlock(false); // scope block
        Next();
        StatementList();
        Scanner.CheckMatch(TkUntil, TkRepeat, line);
        var conditionExit = Condition();
        if (Function.Block.HasUpValue)
        {
            Function.PatchClose(conditionExit, Function.Block.ActiveVariableCount);
        }

        Function.LeaveBlock(); // finish scope
        Function.PatchList(conditionExit, top); // close loop
        Function.LeaveBlock(); // finish loop
    }


    public int Condition()
    {
        var e = Expression();
        if (e.Kind == Kind.Nil)
        {
            e.Kind = Kind.False;
        }

        return Function.GoIfTrue(e).F;
    }


    public void GotoStatement(int pc)
    {
        var line = Scanner.LineNumber;
        if (TestNext(TkGoto))
        {
            Function.MakeGoto(CheckName(), line, pc);
        }
        else
        {
            Next();
            Function.MakeGoto("break", line, pc);
        }
    }


    public void SkipEmptyStatements()
    {
        while (T == ';' || T == TkDoubleColon)
        {
            Statement();
        }
    }


    public void LabelStatement(string label, int line)
    {
        Function.CheckRepeatedLabel(label);
        CheckNext(TkDoubleColon);
        var l = Function.MakeLabel(label, line);
        SkipEmptyStatements();
        if (BlockFollow(false))
        {
            ActiveLabels[l].ActiveVariableCount = Function.Block.ActiveVariableCount;
        }

        Function.FindGotos(l);
    }


    public void ParameterList()
    {
        var n = 0;
        var isVarArg = false;
        if (T != ')')
        {
            for (var first = true; first || (!isVarArg && TestNext(',')); first = false)
            {
                switch (T)
                {
                    case TkName:
                        Function.MakeLocalVariable(CheckName());
                        n++;
                        break;
                    case TkDots:
                        Next();
                        isVarArg = true;
                        break;
                    default:
                        Scanner.SyntaxError("<name> or '...' expected");
                        break;
                }
            }
        }

        // TODO the following lines belong in a *function method
        Function.Proto.IsVarArg = isVarArg;
        Function.AdjustLocalVariables(n);
        Function.Proto.ParameterCount = Function.ActiveVariableCount;
        Function.ReserveRegisters(Function.ActiveVariableCount);
    }


    public ExprDesc Body(bool isMethod, int line)
    {
        Function.OpenFunction(line);
        CheckNext('(');
        if (isMethod)
        {
            Function.MakeLocalVariable("self");
            Function.AdjustLocalVariables(1);
        }

        ParameterList();
        CheckNext(')');
        StatementList();
        Function.Proto.LastLineDefined = Scanner.LineNumber;
        Scanner.CheckMatch(TkEnd, TkFunction, line);
        return Function.CloseFunction();
    }


    public (ExprDesc, bool IsMethod) FunctionName()
    {
        var e = SingleVariable();
        for (; T == '.'; e = FieldSelector(e)) ;
        if (T == ':')
        {
            e = FieldSelector(e);
            return (e, true);
        }

        return (e, false);
    }


    public void FunctionStatement(int line)
    {
        Next();
        var (v, m) = FunctionName();
        Function.StoreVariable(v, Body(m, line));
        Function.FixLine(line);
    }


    public void LocalFunction()
    {
        Function.MakeLocalVariable(CheckName());
        Function.AdjustLocalVariables(1);
        Function.LocalVariable(Body(false, Scanner.LineNumber).Info).StartPc = (Function.Proto.CodeList.Length);
    }


    public void LocalStatement()
    {
        var v = 0;
        for (var first = true; first || TestNext(','); first = false)
        {
            Function.MakeLocalVariable(CheckName());
            v++;
        }

        if (TestNext('='))
        {
            var (e, n) = ExpressionList();
            Function.AdjustAssignment(v, n, e);
        }
        else
        {
            var e = default(ExprDesc);
            Function.AdjustAssignment(v, 0, e);
        }

        Function.AdjustLocalVariables(v);
    }


    public void ExpressionStatement()
    {
        var e = SuffixedExpression();
        if (T == '=' || T == ',')
        {
            Assignment(new AssignmentTarget(ref Unsafe.NullRef<AssignmentTarget>(), exprDesc: e), 1);
        }
        else
        {
            CheckCondition(e.Kind == Kind.Call, "syntax error");
            Function.Instruction(e).C = (1); // call statement uses no results
        }
    }


    public void ReturnStatement()
    {
        var f = Function;
        if (BlockFollow(true) || T == ';')
        {
            f.ReturnNone();
        }
        else
        {
            var (e, n) = ExpressionList();
            f.Return(e, n);
        }

        TestNext(';');
    }


    public void Statement()
    {
        var line = Scanner.LineNumber;
        using var _ = EnterLevel();
        switch (T)
        {
            case ';':
                Next();
                break;
            case TkIf:
                IfStatement(line);
                break;
            case TkWhile:
                WhileStatement(line);
                break;
            case TkDo:
                Next();
                Block();
                Scanner.CheckMatch(TkEnd, TkDo, line);
                break;
            case TkFor:
                ForStatement(line);
                break;
            case TkRepeat:
                RepeatStatement(line);
                break;
            case TkFunction:
                FunctionStatement(line);
                break;
            case TkLocal:
                Next();
                if (TestNext(TkFunction))
                {
                    LocalFunction();
                }
                else
                {
                    LocalStatement();
                }

                break;
            case TkDoubleColon:
                Next();
                LabelStatement(CheckName(), line);
                break;
            case TkReturn:
                Next();
                ReturnStatement();
                break;
            case TkBreak:
            case TkGoto:
                GotoStatement(Function.Jump());
                break;
            default:
                ExpressionStatement();
                break;
        }

        Assert(Function.Proto.MaxStackSize >= Function.FreeRegisterCount && Function.FreeRegisterCount >= Function.ActiveVariableCount);
        Function.FreeRegisterCount = Function.ActiveVariableCount;
    }


    internal void MainFunction()
    {
        Function.OpenMainFunction();
        Next();
        StatementList();
        Scanner.Check(TkEos);
        Function = Function.CloseMainFunction();
    }

    public static Prototype Parse(LuaState l, TextReader r, string name)
    {
        using var p = Get(new()
        {
            R = r,
            LineNumber = 1,
            LastLine = 1,
            LookAheadToken = new() { T = TkEos },
            L = l,
            Buffer = new(),
            Source = name
        });
        var f = Function.Get(p, PrototypeBuilder.Get(name));
        p.Function = f;
        p.MainFunction();
        f.Proto.IsVarArg = true;
        f.Proto.LineDefined = 0;
        return f.Proto.CreatePrototypeAndRelease();
    }


    public static void Dump(Prototype prototype, IBufferWriter<byte> writer, bool useLittleEndian = true)
    {
        var state = new DumpState(writer, useLittleEndian ^ BitConverter.IsLittleEndian);
        state.Dump(prototype);
    }

    public static byte[] Dump(Prototype prototype, bool useLittleEndian = true)
    {
        var writer = new ArrayBufferWriter<byte>();
        Dump(prototype, writer, useLittleEndian);
        return writer.WrittenSpan.ToArray();
    }

    public static Prototype UnDump(ReadOnlySpan<byte> span, ReadOnlySpan<char> name)
    {
        if (name.Length > 0)
        {
            name = name[0] switch
            {
                '@' or '=' => name[1..],
                '\e' => "binary string",
                _ => name
            };
        }

        var state = new UnDumpState(span, name);
        return state.UnDump();
    }
}