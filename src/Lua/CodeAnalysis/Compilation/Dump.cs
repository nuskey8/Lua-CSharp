using System.Buffers;
using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using Lua.Internal;
using Lua.IO;
using Lua.Runtime;

namespace Lua.CodeAnalysis.Compilation;

[SuppressMessage("ReSharper", "MemberCanBePrivate.Local")]
[StructLayout(LayoutKind.Sequential, Pack = 1)]
unsafe struct Header
{
    public static ReadOnlySpan<byte> LuaSignature => "\eLua"u8;
    public static ReadOnlySpan<byte> LuaTail => [0x19, 0x93, 0x0d, 0x0a, 0x1a, 0x0a];

    public fixed byte Signature[4];
    public byte Version,
        Format,
        Endianness,
        IntSize;
    public byte PointerSize,
        InstructionSize;
    public byte NumberSize,
        IntegralNumber;
    public fixed byte Tail[6];

    public const int Size = 18;

    public Header(bool isLittleEndian)
    {
        fixed (byte* signature = Signature)
        {
            LuaSignature.CopyTo(new(signature, 4));
        }

        Version = (Constants.VersionMajor << 4) | Constants.VersionMinor;
        Format = 0;
        Endianness = (byte)(isLittleEndian ? 1 : 0);
        IntSize = 4;
        PointerSize = (byte)sizeof(IntPtr);
        InstructionSize = 4;
        NumberSize = 8;
        IntegralNumber = 0;
        fixed (byte* tail = Tail)
        {
            LuaTail.CopyTo(new(tail, 6));
        }
    }

    public void Validate(ReadOnlySpan<char> name)
    {
        fixed (byte* signature = Signature)
        {
            if (!LuaSignature.SequenceEqual(new(signature, 4)))
            {
                throw new LuaUndumpException($"{name.ToString()}: is not a precompiled chunk");
            }
        }

        var major = Version >> 4;
        var minor = Version & 0xF;
        if (major != Constants.VersionMajor || minor != Constants.VersionMinor)
        {
            throw new LuaUndumpException(
                $"{name.ToString()}: version mismatch in precompiled chunk {major}.{minor} != {Constants.VersionMajor}.{Constants.VersionMinor}"
            );
        }

        if (
            IntSize != 4
            || Format != 0
            || IntegralNumber != 0
            || PointerSize is not (4 or 8)
            || InstructionSize != 4
            || NumberSize != 8
            || Endianness > 1
        )
        {
            goto ErrIncompatible;
        }

        fixed (byte* tail = Tail)
        {
            if (!LuaTail.SequenceEqual(new(tail, 6)))
            {
                goto ErrIncompatible;
            }
        }

        return;
        ErrIncompatible:
        throw new LuaUndumpException($"{name.ToString()}: incompatible precompiled chunk");
    }
}

unsafe ref struct DumpState(
    IBufferWriter<byte> writer,
    bool reversedEndian,
    LuaStringMode stringMode
)
{
    readonly Encoding encoding =
        stringMode == LuaStringMode.Bytes ? LuaByteEncoding.Instance : Encoding.UTF8;
    public readonly IBufferWriter<byte> Writer = writer;
    Span<byte> unWritten;

    void Write(ReadOnlySpan<byte> span)
    {
        var toWrite = span;
        var remaining = unWritten.Length;
        if (span.Length > remaining)
        {
            span[..remaining].CopyTo(unWritten);
            Writer.Advance(remaining);
            toWrite = span[remaining..];
            unWritten = Writer.GetSpan(toWrite.Length);
        }

        toWrite.CopyTo(unWritten);
        Writer.Advance(toWrite.Length);
        unWritten = unWritten[toWrite.Length..];
    }

    public bool IsReversedEndian => reversedEndian;

    void DumpHeader()
    {
        Header header = new(BitConverter.IsLittleEndian ^ IsReversedEndian);
        Write(new(&header, Header.Size));
    }

    public void Dump(Prototype prototype)
    {
        if (unWritten.Length == 0)
        {
            unWritten = Writer.GetSpan(Header.Size + 32);
        }

        DumpHeader();
        DumpFunction(prototype);
    }

    void DumpFunction(Prototype prototype)
    {
        WriteInt(prototype.LineDefined); // 4
        WriteInt(prototype.LastLineDefined); // 4
        WriteByte((byte)prototype.ParameterCount); // 1
        WriteByte((byte)(prototype.HasVariableArguments ? 1 : 0)); // 1
        WriteByte((byte)prototype.MaxStackSize); // 1
        WriteIntSpanWithLength(MemoryMarshal.Cast<Instruction, int>(prototype.Code)); // 4
        WriteConstants(prototype.Constants); // 4
        WritePrototypes(prototype.ChildPrototypes); // 4
        WriteUpValues(prototype.UpValues); // 4

        // Debug
        WriteString(prototype.ChunkName);
        WriteIntSpanWithLength(prototype.LineInfo);
        WriteLocalVariables(prototype.LocalVariables);
        WriteInt(prototype.UpValues.Length);
        foreach (var desc in prototype.UpValues)
        {
            WriteString(desc.Name);
        }
    }

    void WriteInt(int v)
    {
        if (reversedEndian)
        {
            v = BinaryPrimitives.ReverseEndianness(v);
        }

        Write(new(&v, sizeof(int)));
    }

    void WriteLong(long v)
    {
        if (reversedEndian)
        {
            v = BinaryPrimitives.ReverseEndianness(v);
        }

        Write(new(&v, sizeof(long)));
    }

    void WriteByte(byte v)
    {
        Write(new(&v, sizeof(byte)));
    }

    void WriteDouble(double v)
    {
        var l = BitConverter.DoubleToInt64Bits(v);
        WriteLong(l);
    }

    void WriteIntSpanWithLength(ReadOnlySpan<int> v)
    {
        WriteInt(v.Length);
        if (IsReversedEndian)
        {
            foreach (var i in v)
            {
                var reversed = BinaryPrimitives.ReverseEndianness(i);
                Write(new(&reversed, 4));
            }
        }
        else
        {
            Write(MemoryMarshal.Cast<int, byte>(v));
        }
    }

    void WriteBool(bool v)
    {
        WriteByte(v ? (byte)1 : (byte)0);
    }

    void WriteString(string v)
    {
        var bytes = encoding.GetBytes(v);
        var len = bytes.Length + 1;

        if (sizeof(IntPtr) == 8)
        {
            WriteLong(len);
        }
        else
        {
            WriteInt(len);
        }

        Write(bytes);
        WriteByte(0);
    }

    void WriteConstants(ReadOnlySpan<LuaValue> constants)
    {
        WriteInt(constants.Length);
        foreach (var c in constants)
        {
            // Lua bytecode tags differ from LuaValueType for strings.
            switch (c.Type)
            {
                case LuaValueType.Nil:
                    WriteByte(0);
                    break;
                case LuaValueType.Boolean:
                    WriteByte(1);
                    WriteBool(c.UnsafeReadDouble() != 0);
                    break;
                case LuaValueType.Number:
                    WriteByte(3);
                    WriteDouble(c.UnsafeReadDouble());
                    break;
                case LuaValueType.String:
                    WriteByte(4);
                    WriteString(c.UnsafeRead<string>());
                    break;
                default:
                    throw new ArgumentException(
                        "Unsupported bytecode constant type",
                        nameof(constants)
                    );
            }
        }
    }

    void WritePrototypes(ReadOnlySpan<Prototype> prototypes)
    {
        WriteInt(prototypes.Length);
        foreach (var p in prototypes)
        {
            DumpFunction(p);
        }
    }

    void WriteLocalVariables(ReadOnlySpan<LocalVariable> localVariables)
    {
        WriteInt(localVariables.Length);
        foreach (var v in localVariables)
        {
            WriteString(v.Name);
            WriteInt(v.StartPc);
            WriteInt(v.EndPc);
        }
    }

    void WriteUpValues(ReadOnlySpan<UpValueDesc> upValues)
    {
        WriteInt(upValues.Length);
        foreach (var u in upValues)
        {
            WriteBool(u.IsLocal);
            WriteByte((byte)u.Index);
        }
    }
}

unsafe ref struct UndumpState(
    ReadOnlySpan<byte> span,
    ReadOnlySpan<char> name,
    StringInternPool internPool,
    LuaStringMode stringMode
)
{
    readonly Encoding encoding =
        stringMode == LuaStringMode.Bytes ? LuaByteEncoding.Instance : Encoding.UTF8;
    public ReadOnlySpan<byte> Unread = span;
    bool otherEndian;
    int pointerSize;
    readonly ReadOnlySpan<char> name = name;

    [DoesNotReturn]
    void Throw(string why)
    {
        throw new LuaUndumpException($"{name.ToString()}: {why} precompiled chunk");
    }

    void ThrowTooShort()
    {
        Throw("truncate");
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal void Read(Span<byte> dst)
    {
        if (Unread.Length < dst.Length)
        {
            ThrowTooShort();
        }

        Unread[..dst.Length].CopyTo(dst);

        Unread = Unread[dst.Length..];
    }

    byte ReadByte()
    {
        if (0 < Unread.Length)
        {
            var b = Unread[0];
            Unread = Unread[1..];
            return b;
        }

        ThrowTooShort();
        return 0;
    }

    bool ReadBool()
    {
        var value = ReadByte();
        if (value > 1)
            Throw("invalid boolean in");
        return value != 0;
    }

    int ReadInt()
    {
        var i = 0;
        Span<byte> span = new(&i, sizeof(int));
        Read(span);

        if (otherEndian)
        {
            i = BinaryPrimitives.ReverseEndianness(i);
        }

        return i;
    }

    // Check serialized lengths against the available input before allocating.
    // Dividing avoids overflow when a corrupt count is close to int.MaxValue.
    int ReadCount(int minimumElementSize)
    {
        var count = ReadInt();
        if ((uint)count > (uint)(Unread.Length / minimumElementSize))
            Throw("invalid length in");
        return count;
    }

    long ReadLong()
    {
        long i = 0;
        Span<byte> span = new(&i, sizeof(long));
        Read(span);

        if (otherEndian)
        {
            i = BinaryPrimitives.ReverseEndianness(i);
        }

        return i;
    }

    double ReadDouble()
    {
        var i = ReadLong();

        return *(double*)&i;
    }

    public Prototype Undump()
    {
        Header h = default;
        Span<byte> span = new(&h, sizeof(Header));
        Read(span);

        h.Validate(name);
        otherEndian = BitConverter.IsLittleEndian ^ (h.Endianness == 1);
        pointerSize = h.PointerSize;
        return UndumpFunction(0);
    }

    Prototype UndumpFunction(int depth)
    {
        if (depth >= Constants.MaxCallCount || !RuntimeHelpers.TryEnsureSufficientExecutionStack())
            Throw("excessive nesting in");
        var lineDefined = ReadInt(); // 4
        var lastLineDefined = ReadInt(); // 4
        var parameterCount = ReadByte(); // 1
        var isVarArg = ReadBool(); // 1
        var maxStackSize = ReadByte(); // 1
        var codeLength = ReadCount(sizeof(int));
        var code = new Instruction[codeLength];
        ReadInToIntSpan(MemoryMarshal.Cast<Instruction, int>(code));
        var constants = ReadConstants();
        var prototypes = ReadPrototypes(depth);
        var upValues = ReadUpValues();

        // Debug
        var source = ReadString();
        var lineInfoLength = ReadCount(sizeof(int));
        if (lineInfoLength != 0 && lineInfoLength != codeLength)
            Throw("invalid line info in");
        var lineInfo = new int[lineInfoLength];
        ReadInToIntSpan(lineInfo.AsSpan());
        var localVariables = ReadLocalVariables();
        var upValueCount = ReadInt();
        if (upValueCount != 0 && upValueCount != upValues.Length)
            Throw("invalid upvalue name count in");
        for (var i = 0; i < upValueCount; i++)
        {
            upValues[i].Name = ReadString();
        }

        var prototype = new Prototype(
            source,
            lineDefined,
            lastLineDefined,
            parameterCount,
            maxStackSize,
            isVarArg,
            constants,
            code,
            prototypes,
            lineInfo,
            localVariables,
            upValues
        )
        {
            StringMode = stringMode,
        };
        new BytecodeVerifier(prototype, name).Verify();
        return prototype;
    }

    void ReadInToIntSpan(Span<int> toWrite)
    {
        Read(MemoryMarshal.AsBytes(toWrite));
        if (otherEndian)
        {
            foreach (ref var value in toWrite)
                value = BinaryPrimitives.ReverseEndianness(value);
        }
    }

    string ReadString()
    {
        var serializedLength = pointerSize == 4 ? ReadInt() : ReadLong();
        if (serializedLength == 0)
        {
            return "";
        }

        if (serializedLength < 0 || serializedLength > Unread.Length)
            Throw("invalid string length in");
        var len = (int)serializedLength;
        if (Unread[len - 1] != 0)
            Throw("unterminated string in");
        var bytes = Unread[..(len - 1)];
        Unread = Unread[len..];
        char[]? charArrayPooled = null;
        try
        {
            // UTF-8 produces no more UTF-16 code units than input bytes. Decode
            // directly from the chunk instead of copying to a second pooled array.
            var chars =
                len <= 128
                    ? stackalloc char[len]
                    : (charArrayPooled = ArrayPool<char>.Shared.Rent(len));
            var count = encoding.GetChars(bytes, chars);
            return internPool.Intern(chars[..count]);
        }
        finally
        {
            if (charArrayPooled != null)
            {
                ArrayPool<char>.Shared.Return(charArrayPooled);
            }
        }
    }

    LuaValue[] ReadConstants()
    {
        var count = ReadCount(1);
        var constants = new LuaValue[count];
        for (var i = 0; i < count; i++)
        {
            var type = ReadByte();
            switch (type)
            {
                case 0:
                    break;
                case 1:
                    constants[i] = ReadBool();
                    break;
                case 3:
                    constants[i] = ReadDouble();
                    break;
                case 4:
                    constants[i] = ReadString();
                    break;
                default:
                    Throw("invalid constant type in");
                    break;
            }
        }

        return constants;
    }

    Prototype[] ReadPrototypes(int depth)
    {
        // Two line numbers, three flags, seven section counts, and the source length.
        const int fixedFunctionSize = 2 * sizeof(int) + 3 + 7 * sizeof(int);
        var count = ReadCount(fixedFunctionSize + pointerSize);
        var prototypes = count != 0 ? new Prototype[count] : [];
        for (var i = 0; i < count; i++)
        {
            prototypes[i] = UndumpFunction(depth + 1);
        }

        return prototypes;
    }

    LocalVariable[] ReadLocalVariables()
    {
        var count = ReadCount(pointerSize + 2 * sizeof(int));
        var localVariables = new LocalVariable[count];
        for (var i = 0; i < count; i++)
        {
            var name = ReadString();
            var startPc = ReadInt();
            var endPc = ReadInt();
            localVariables[i] = new()
            {
                Name = name,
                StartPc = startPc,
                EndPc = endPc,
            };
        }

        return localVariables;
    }

    UpValueDesc[] ReadUpValues()
    {
        var count = ReadCount(2);
        if (count > Constants.MaxUpValue)
            Throw("too many upvalues in");
        var upValues = new UpValueDesc[count];
        for (var i = 0; i < count; i++)
        {
            var isLocal = ReadBool();
            var index = ReadByte();
            upValues[i] = new()
            {
                IsLocal = isLocal,
                Index = index,
                Name = "",
            };
        }

        return upValues;
    }
}
