using Lua.Internal;
using Lua.Runtime;
using System.Buffers;
using System.Buffers.Binary;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace Lua.CodeAnalysis.Compilation;

[SuppressMessage("ReSharper", "MemberCanBePrivate.Local")]
[StructLayout(LayoutKind.Sequential, Pack = 1)]
unsafe struct Header
{
    public static ReadOnlySpan<byte> LuaSignature => "\eLua"u8;

    public static ReadOnlySpan<byte> LuaTail => [0x19, 0x93, 0x0d, 0x0a, 0x1a, 0x0a];

    public fixed byte Signature[4];
    public byte Version, Format, Endianness, IntSize;
    public byte PointerSize, InstructionSize;
    public byte NumberSize, IntegralNumber;
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
                throw new LuaUnDumpException($"{name.ToString()}: is not a precompiled chunk");
            }
        }

        var major = Version >> 4;
        var minor = Version & 0xF;
        if (major != Constants.VersionMajor || minor != Constants.VersionMinor)
        {
            throw new LuaUnDumpException($"{name.ToString()}: version mismatch in precompiled chunk {major}.{minor} != {Constants.VersionMajor}.{Constants.VersionMinor}");
        }

        if (IntSize != 4 || Format != 0 || IntegralNumber != 0 || PointerSize is not (4 or 8) || InstructionSize != 4 || NumberSize != 8)
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
        throw new LuaUnDumpException($"{name.ToString()}: incompatible precompiled chunk");
    }
}

unsafe ref struct DumpState(IBufferWriter<byte> writer, bool reversedEndian)
{
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
        WriteInt(prototype.LineDefined); //4
        WriteInt(prototype.LastLineDefined); //4
        WriteByte((byte)prototype.ParameterCount); //1
        WriteByte((byte)prototype.MaxStackSize); //1
        WriteByte((byte)(prototype.HasVariableArguments ? 1 : 0)); //1
        WriteIntSpanWithLength(MemoryMarshal.Cast<Instruction, int>(prototype.Code)); //4
        WriteConstants(prototype.Constants); //4
        WritePrototypes(prototype.ChildPrototypes); //4
        WriteUpValues(prototype.UpValues); //4

        //Debug
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
        var bytes = Encoding.UTF8.GetBytes(v);
        var len = bytes.Length;
        if (bytes.Length != 0)
        {
            len++;
        }

        if (sizeof(IntPtr) == 8)
        {
            WriteLong(len);
        }
        else
        {
            WriteInt(len);
        }

        if (len != 0)
        {
            Write(bytes);
            WriteByte(0);
        }
    }

    void WriteConstants(ReadOnlySpan<LuaValue> constants)
    {
        WriteInt(constants.Length);
        foreach (var c in constants)
        {
            WriteByte((byte)c.Type);
            switch (c.Type)
            {
                case LuaValueType.Nil: break;
                case LuaValueType.Boolean:
                    WriteBool(c.UnsafeReadDouble() != 0);
                    break;
                case LuaValueType.Number:
                    WriteDouble(c.UnsafeReadDouble());
                    break;
                case LuaValueType.String:
                    WriteString(c.UnsafeRead<string>());
                    break;
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

unsafe ref struct UnDumpState(ReadOnlySpan<byte> span, ReadOnlySpan<char> name, StringInternPool internPool)
{
    public ReadOnlySpan<byte> Unread = span;
    bool otherEndian;
    int pointerSize;
    readonly ReadOnlySpan<char> name = name;

    void Throw(string why)
    {
        throw new LuaUnDumpException($"{name.ToString()}: {why} precompiled chunk");
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
        if (0 < Unread.Length)
        {
            var b = Unread[0];
            Unread = Unread[1..];
            return b != 0;
        }

        ThrowTooShort();

        return false;
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

    public Prototype UnDump()
    {
        Header h = default;
        Span<byte> span = new(&h, sizeof(Header));
        Read(span);

        h.Validate(name);
        otherEndian = BitConverter.IsLittleEndian ^ (h.Endianness == 1);
        pointerSize = h.PointerSize;
        return UnDumpFunction();
    }


    Prototype UnDumpFunction()
    {
        var lineDefined = ReadInt(); //4
        var lastLineDefined = ReadInt(); //4
        var parameterCount = ReadByte(); //1
        var maxStackSize = ReadByte(); //1
        var isVarArg = ReadByte() == 1; //1
        var codeLength = ReadInt();
        var code = new Instruction[codeLength];
        ReadInToIntSpan(MemoryMarshal.Cast<Instruction, int>(code));
        var constants = ReadConstants();
        var prototypes = ReadPrototypes();
        var upValues = ReadUpValues();

        //Debug
        var source = ReadString();
        var lineInfoLength = ReadInt();
        var lineInfo = new int[lineInfoLength];
        ReadInToIntSpan(lineInfo.AsSpan());
        var localVariables = ReadLocalVariables();
        var upValueCount = ReadInt();
        Debug.Assert(upValueCount == upValues.Length, $"upvalue count mismatch: {upValueCount} != {upValues.Length}");
        foreach (ref var desc in upValues.AsSpan())
        {
            var name = ReadString();
            desc.Name = name;
        }

        return new(source, lineDefined, lastLineDefined, parameterCount, maxStackSize, isVarArg, constants, code, prototypes, lineInfo, localVariables, upValues);
    }


    void ReadInToIntSpan(Span<int> toWrite)
    {
        for (var i = 0; i < toWrite.Length; i++)
        {
            toWrite[i] = ReadInt();
        }
    }


    string ReadString()
    {
        var len = pointerSize == 4 ? ReadInt() : (int)ReadLong();
        if (len == 0)
        {
            return "";
        }

        len--;
        var arrayPooled = ArrayPool<byte>.Shared.Rent(len);
        char[]? charArrayPooled = null;
        try
        {
            var span = arrayPooled.AsSpan(0, len);
            Read(span);

            var l = ReadByte();
            Debug.Assert(l == 0);
            var chars = len <= 128 ? stackalloc char[len*2] : (charArrayPooled = ArrayPool<char>.Shared.Rent(len * 2));
            var count = Encoding.UTF8.GetChars(span, chars);
            return internPool.Intern(chars[..count]);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(arrayPooled);
            if (charArrayPooled != null)
            {
                ArrayPool<char>.Shared.Return(charArrayPooled);
            }
        }
    }

    LuaValue[] ReadConstants()
    {
        var count = ReadInt();
        var constants = new LuaValue[count];
        for (var i = 0; i < count; i++)
        {
            var type = (LuaValueType)ReadByte();
            switch (type)
            {
                case LuaValueType.Nil: break;
                case LuaValueType.Boolean:
                    constants[i] = ReadByte() == 1;
                    break;
                case LuaValueType.Number:
                    constants[i] = ReadDouble();
                    break;
                case LuaValueType.String:
                    constants[i] = ReadString();
                    break;
            }
        }

        return constants;
    }

    Prototype[] ReadPrototypes()
    {
        var count = ReadInt();
        var prototypes = count != 0 ? new Prototype[count] : [];
        for (var i = 0; i < count; i++)
        {
            prototypes[i] = UnDumpFunction();
        }

        return prototypes;
    }

    LocalVariable[] ReadLocalVariables()
    {
        var count = ReadInt();
        var localVariables = new LocalVariable[count];
        for (var i = 0; i < count; i++)
        {
            var name = ReadString();
            var startPc = ReadInt();
            var endPc = ReadInt();
            localVariables[i] = new() { Name = name, StartPc = startPc, EndPc = endPc };
        }

        return localVariables;
    }

    UpValueDesc[] ReadUpValues()
    {
        var count = ReadInt();
        Debug.Assert(count < 100, $" too many upvalues :{count}");
        var upValues = new UpValueDesc[count];
        for (var i = 0; i < count; i++)
        {
            var isLocal = ReadBool();
            var index = ReadByte();
            upValues[i] = new() { IsLocal = isLocal, Index = index };
        }

        return upValues;
    }
}