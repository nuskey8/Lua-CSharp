using System.Buffers.Binary;
using Lua.CodeAnalysis;
using Lua.Runtime;
using Lua.Standard;

namespace Lua.Tests;

public class BytecodeValidationTests
{
    static Prototype Create(
        Instruction[] code,
        int stackSize = 2,
        LuaValue[]? constants = null,
        Prototype[]? children = null,
        UpValueDesc[]? upValues = null,
        bool varargs = false
    ) =>
        new(
            "bytecode-test",
            0,
            0,
            0,
            stackSize,
            varargs,
            constants ?? [],
            code,
            children ?? [],
            Enumerable.Repeat(1, code.Length).ToArray(),
            [],
            upValues ?? []
        );

    static IEnumerable<TestCaseData> InvalidInstructions()
    {
        var instructions = new (string, Instruction)[]
        {
            ("read outside stack allocation", Instruction.Move(0, 511)),
            ("write outside register window", Instruction.Move(255, 0)),
            ("constant index", Instruction.LoadK(0, 1)),
            ("missing extra argument", Instruction.LoadKX(0)),
            ("nil register range", Instruction.LoadNil(1, 1)),
            ("upvalue read", Instruction.GetUpVal(0, 0)),
            ("upvalue write", Instruction.SetUpVal(0, 0)),
            ("method receiver slot", Instruction.Self(1, 0, 0)),
            ("arithmetic constant", Instruction.Add(0, 256, 0)),
            ("arithmetic register", Instruction.Add(0, 0, 2)),
            ("unary register", Instruction.Unm(0, 2)),
            ("reversed concatenation", Instruction.Concat(0, 1, 0)),
            ("backward jump", Instruction.Jmp(0, -2)),
            ("forward jump", Instruction.Jmp(0, 1)),
            ("call arguments", Instruction.Call(0, 3, 1)),
            ("call results", Instruction.Call(0, 1, 4)),
            ("return range", Instruction.Return(1, 3)),
            ("numeric loop window", Instruction.ForPrep(0, 0)),
            ("iterator window", Instruction.TForCall(0, 1)),
            ("constructor values", Instruction.SetList(0, 2, 1)),
            ("missing constructor block", Instruction.SetList(0, 1, 0)),
            ("child prototype", Instruction.Closure(0, 0)),
            ("vararg in fixed function", Instruction.VarArg(0, 2)),
            ("unattached extra argument", Instruction.ExtraArg(0)),
            ("unknown opcode", new Instruction(63)),
        };
        foreach (var (name, instruction) in instructions)
            yield return new TestCaseData(new[] { instruction, Instruction.Return(0, 1) }).SetName(
                "Rejects_" + name.Replace(' ', '_')
            );
        yield return new TestCaseData(new[] { Instruction.Move(0, 0) }).SetName(
            "Rejects_fallthrough_past_code"
        );
        yield return new TestCaseData(
            new[] { Instruction.LoadBool(0, 1, 1), Instruction.Return(0, 1) }
        ).SetName("Rejects_skip_past_code");
        yield return new TestCaseData(
            new[]
            {
                Instruction.Jmp(0, 1),
                Instruction.LoadKX(0),
                Instruction.ExtraArg(0),
                Instruction.Return(0, 1),
            }
        ).SetName("Rejects_jump_into_extra_argument");
        yield return new TestCaseData(
            new[] { Instruction.LoadKX(0), Instruction.ExtraArg(0), Instruction.Return(0, 1) }
        ).SetName("Rejects_extra_constant_index");
    }

    [TestCaseSource(nameof(InvalidInstructions))]
    public void RejectsInvalidOperandsBeforeExecution(Instruction[] code)
    {
        var bytes = Create(code).ToBytecode();
        using var state = LuaState.Create();
        Assert.Throws<LuaUndumpException>(() => state.Load(bytes));
    }

    [TestCase(true)]
    [TestCase(false)]
    public async Task ExtraArgumentsAndBothByteOrdersRoundTrip(bool littleEndian)
    {
        var prototype = Create(
            [
                Instruction.NewTable(0, 0, 0),
                Instruction.LoadKX(1),
                Instruction.ExtraArg(0),
                Instruction.SetList(0, 1, 0),
                Instruction.ExtraArg(1),
                Instruction.Return(0, 2),
            ],
            constants: [42]
        );
        using var state = LuaState.Create();
        var values = await state.ExecuteAsync(state.Load(prototype.ToBytecode(littleEndian)));
        Assert.That(values[0].Read<LuaTable>()[1], Is.EqualTo(new LuaValue(42)));
    }

    [TestCase(true)]
    [TestCase(false)]
    public void RejectsChildCapturesOutsideParent(bool local)
    {
        var child = Create(
            [Instruction.Return(0, 1)],
            upValues:
            [
                new()
                {
                    IsLocal = local,
                    Index = 2,
                    Name = "x",
                },
            ]
        );
        var bytes = Create([Instruction.Return(0, 1)], children: [child]).ToBytecode();
        Assert.Throws<LuaUndumpException>(() => Prototype.FromBytecode(bytes, "capture"));
    }

    [TestCase(-1)]
    [TestCase(int.MaxValue)]
    public void RejectsInstructionCountsBeforeAllocating(int count)
    {
        var bytes = Create([Instruction.Return(0, 1)]).ToBytecode();
        // Header, two line numbers, three byte-sized function fields.
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(29), count);
        Assert.Throws<LuaUndumpException>(() => Prototype.FromBytecode(bytes, "count"));
    }

    [TestCase(-1L)]
    [TestCase(4294967296L)]
    [TestCase(long.MaxValue)]
    public void RejectsStringLengthsWithoutNarrowing(long length)
    {
        var bytes = Create([Instruction.Return(0, 1)]).ToBytecode();
        // Skip the instruction count/body and three empty sections to the source string.
        const int sourceLengthOffset = 49;
        if (IntPtr.Size == 8)
            BinaryPrimitives.WriteInt64LittleEndian(bytes.AsSpan(sourceLengthOffset), length);
        else
            BinaryPrimitives.WriteInt32LittleEndian(
                bytes.AsSpan(sourceLengthOffset),
                unchecked((int)length)
            );
        Assert.Throws<LuaUndumpException>(() => Prototype.FromBytecode(bytes, "string"));
    }

    [Test]
    public void RejectsUnknownConstantTags()
    {
        var bytes = Create([Instruction.Return(0, 1)], constants: [42]).ToBytecode();
        bytes[41] = 255;
        Assert.Throws<LuaUndumpException>(() => Prototype.FromBytecode(bytes, "constant"));
    }

    [TestCase(LuaValueType.Nil, 0)]
    [TestCase(LuaValueType.Boolean, 1)]
    [TestCase(LuaValueType.Number, 3)]
    [TestCase(LuaValueType.String, 4)]
    public void ConstantTagsFollowLua52RatherThanTheInternalValueEnum(LuaValueType type, int tag)
    {
        LuaValue value = type switch
        {
            LuaValueType.Nil => LuaValue.Nil,
            LuaValueType.Boolean => true,
            LuaValueType.Number => 42,
            _ => "hello",
        };
        var bytes = Create([Instruction.Return(0, 1)], constants: [value]).ToBytecode();
        // Header, function fields, one instruction, and the constant count.
        Assert.That(bytes[41], Is.EqualTo(tag));
        Assert.That(Prototype.FromBytecode(bytes, "constant").Constants[0], Is.EqualTo(value));
        bytes[41] = 2; // Lua's light userdata tag cannot occur in a serialized constant.
        Assert.Throws<LuaUndumpException>(() => Prototype.FromBytecode(bytes, "constant"));
    }

    [TestCase("", 1)]
    [TestCase("a", 2)]
    [TestCase("\0", 2)]
    public void StringConstantLengthsIncludeTheirTerminator(string value, int length)
    {
        var bytes = Create([Instruction.Return(0, 1)], constants: [value]).ToBytecode();
        const int lengthOffset = 42; // Immediately after the first constant's tag.
        var serializedLength =
            IntPtr.Size == 8
                ? BinaryPrimitives.ReadInt64LittleEndian(bytes.AsSpan(lengthOffset))
                : BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(lengthOffset));
        Assert.That(serializedLength, Is.EqualTo(length));
        Assert.That(bytes[lengthOffset + IntPtr.Size + length - 1], Is.Zero);
        Assert.That(
            Prototype.FromBytecode(bytes, "constant").Constants[0],
            Is.EqualTo(new LuaValue(value))
        );
    }

    [Test]
    public void RejectsExcessivelyNestedPrototypes()
    {
        var prototype = Create([Instruction.Return(0, 1)]);
        for (var i = 0; i < 210; i++)
            prototype = Create([Instruction.Return(0, 1)], children: [prototype]);
        var bytes = prototype.ToBytecode();
        Assert.Throws<LuaUndumpException>(() => Prototype.FromBytecode(bytes, "nesting"));
    }

    [Test]
    public void RejectsEveryTruncatedPrefix()
    {
        var bytes = Create(
                [Instruction.LoadK(0, 0), Instruction.Return(0, 2)],
                constants: ["hello"]
            )
            .ToBytecode();
        for (var length = 0; length < bytes.Length; length++)
        {
            var truncated = bytes[..length];
            Assert.Throws<LuaUndumpException>(() => Prototype.FromBytecode(truncated, "truncated"));
        }
    }

    [Test]
    public void MutatedChunksDoNotLeakHostExceptions()
    {
        using var state = LuaState.Create();
        var bytes = state
            .Load("local function f(x) return x+1 end; return f(41)", "mutation")
            .Proto.ToBytecode();
        var random = new Random(42);
        for (var i = 0; i < 1000; i++)
        {
            var mutated = (byte[])bytes.Clone();
            mutated[random.Next(mutated.Length)] ^= (byte)random.Next(1, 256);
            try
            {
                Prototype.FromBytecode(mutated, "mutation");
            }
            catch (LuaUndumpException) { }
        }
    }

    [Test]
    public async Task LoadedChunkPreservesCallsClosuresAndLoops()
    {
        using var state = LuaState.Create();
        state.OpenStandardLibraries();
        var closure = state.Load(
            """
            local t = {total = 0}
            function t:add(x) self.total = self.total + x end
            local function run(offset, ...)
                local n = offset
                local function add(x) n = n + x end
                for i, v in ipairs({...}) do
                    if i <= 3 and v ~= 0 then add(v) end
                end
                for i = 1, 10 do t:add(i) end
                return n + t.total
            end
            return run(10, 1, 2, 3, 100)
            """,
            "workload"
        );
        var loaded = state.Load(closure.Proto.ToBytecode());
        Assert.That((await state.ExecuteAsync(loaded))[0], Is.EqualTo(new LuaValue(71)));
    }

    [TestCase("code.lua")]
    [TestCase("closure.lua")]
    [TestCase("constructs.lua")]
    [TestCase("vararg.lua")]
    [TestCase("goto.lua")]
    [TestCase("nextvar.lua")]
    public void CompilerFixturesPassVerification(string file)
    {
        using var state = LuaState.Create();
        var source = File.ReadAllText(FileHelper.GetAbsolutePath("tests-lua/" + file));
        var prototype = state.Load(source, file).Proto;
        Assert.DoesNotThrow(() => Prototype.FromBytecode(prototype.ToBytecode(), file));
    }
}
