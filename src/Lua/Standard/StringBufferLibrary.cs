using System.Runtime.CompilerServices;
using System.Text;
using System.Diagnostics;
using System.Collections.ObjectModel;
using Lua.Runtime;

namespace Lua.Standard;

using LightUserData = (double value, object referenceValue);

public abstract class StringBuffer : IDisposable
{
    /// <summary>
    /// Gets current length of the buffer. (number of bytes available for reading)
    /// </summary>
    public abstract int Length { get; }

    /// <summary>
    /// Gets or sets the capacity of the buffer.
    /// </summary>
    public abstract int Capacity { get; set; }

    /// <summary>
    /// Called when preparing for reading.
    /// </summary>
    public abstract void BeginRead(LuaState state);

    /// <summary>
    /// Called when reading ended.
    /// </summary>
    public abstract void EndRead();

    /// <summary>
    /// Called when preparing for writing.
    /// </summary>
    public abstract void BeginWrite(LuaState state);

    /// <summary>
    /// Called when writing ended.
    /// </summary>
    public abstract void EndWrite();

    /// <summary>
    /// Skips a certain number of bytes for reading.
    /// </summary>
    public abstract void SkipRead(int length);

    public abstract bool ReadBoolean();

    public abstract byte ReadByte();

    public abstract double ReadDouble();

    public abstract short ReadInt16();

    public abstract int ReadInt32();

    public abstract long ReadInt64();

    public abstract sbyte ReadSByte();

    public abstract float ReadSingle();

    public abstract string ReadString(int length);

    public abstract ushort ReadUInt16();

    public abstract uint ReadUInt32();

    public abstract ulong ReadUInt64();

    public abstract LightUserData ReadLightUserData();

    public abstract ILuaUserData ReadUserData();

    public abstract void WriteBoolean(bool boolean);

    public abstract void WriteByte(byte @byte);

    public abstract void WriteDouble(double @double);

    public abstract void WriteInt32(int int32);

    public abstract void WriteInt64(long int64);

    public abstract void WriteInt16(short int16);

    public abstract void WriteSByte(sbyte @sbyte);

    public abstract void WriteSingle(float single);

    public abstract void WriteString(string @string);

    public abstract void WriteUInt32(uint uint32);

    public abstract void WriteUInt64(ulong uint64);

    public abstract void WriteUInt16(ushort uint16);

    public abstract void WriteLightUserData(LightUserData lightUserData);

    public abstract void WriteUserData(ILuaUserData userData);

    public abstract void WriteChars(ReadOnlySpan<char> chars);

    public abstract void Reset();

    public abstract void Dispose();
}

/// <summary>
/// The basic string buffer object.
/// <para>
/// If you want to add your own serialization for userdata, override the following methods:
/// <see cref="ReadUserData"/>
/// <see cref="ReadLightUserData"/>
/// <see cref="WriteUserData(ILuaUserData)"/>
/// <see cref="WriteLightUserData(LightUserData)"/>
/// </para>
/// </summary>
public class BasicStringBuffer : StringBuffer
{
    private readonly MemoryStream stream;

    private readonly BinaryReader reader;

    private readonly BinaryWriter writer;

    private LuaState? state = null;

    private bool isReading = false;
    private bool isWriting = false;

    private int readPosition = 0;

    private string? cache = null;

    private bool disposed = false;

    public override int Length
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => (int)stream.Length - readPosition;
    }

    public override int Capacity
    {
        get => stream.Capacity;
        set => stream.Capacity = value;
    }

    public BasicStringBuffer()
    {
        stream = new MemoryStream();
        reader = new BinaryReader(stream, Encoding.UTF8, true);
        writer = new BinaryWriter(stream, Encoding.UTF8, true);
    }

    ~BasicStringBuffer()
    {
        Dispose();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override void BeginRead(LuaState state)
    {
        Debug.Assert(!isReading && !isWriting);
        this.state = state;
        isReading = true;
        stream.Position = readPosition;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override void EndRead()
    {
        Debug.Assert(isReading && !isWriting);
        isReading = false;
        cache = null;
        state = null;
        readPosition = (int)stream.Position;
        TryShrink();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override void BeginWrite(LuaState state)
    {
        Debug.Assert(!isReading && !isWriting);
        this.state = state;
        isWriting = true;
        stream.Position = stream.Length;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override void EndWrite()
    {
        Debug.Assert(!isReading && isWriting);
        isWriting = false;
        cache = null;
        state = null;
        writer.Flush();
        TryShrink();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override void SkipRead(int length)
    {
        readPosition = Math.Min(readPosition + length, (int)stream.Length);
        cache = null;
    }

    private void TryShrink()
    {
        long remaining = stream.Length - readPosition;
        if (remaining <= 0)
        {
            stream.SetLength(0);
            stream.Position = 0;
            readPosition = 0;
            return;
        }

        if (stream.Length < 256L || readPosition <= stream.Length / 2L)
        {
            return;
        }

        if (!stream.TryGetBuffer(out ArraySegment<byte> buffer))
        {
            throw new Exception("internal memory stream has no accessible buffer");
        }

        Buffer.BlockCopy(buffer.Array!, buffer.Offset + (int)readPosition, buffer.Array!, buffer.Offset, (int)remaining);

        stream.SetLength(remaining);
        stream.Position = remaining;
        readPosition = 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override bool ReadBoolean() => reader.ReadBoolean();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override byte ReadByte() => reader.ReadByte();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override double ReadDouble() => reader.ReadDouble();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override short ReadInt16() => reader.ReadInt16();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override int ReadInt32() => reader.ReadInt32();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override long ReadInt64() => reader.ReadInt64();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override sbyte ReadSByte() => reader.ReadSByte();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override float ReadSingle() => reader.ReadSingle();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override string ReadString(int length)
    {
        return Encoding.UTF8.GetString(reader.ReadBytes(length));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override ushort ReadUInt16() => reader.ReadUInt16();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override uint ReadUInt32() => reader.ReadUInt32();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override ulong ReadUInt64() => reader.ReadUInt64();

    public override LightUserData ReadLightUserData()
    {
        throw new LuaRuntimeException(state, $"attempt to deserialize unsupported object type: {LuaValueType.LightUserData}");
    }

    public override ILuaUserData ReadUserData()
    {
        throw new LuaRuntimeException(state, $"attempt to deserialize unsupported object type: {LuaValueType.UserData}");
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override void WriteBoolean(bool boolean) => writer.Write(boolean);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override void WriteByte(byte @byte) => writer.Write(@byte);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override void WriteDouble(double @double) => writer.Write(@double);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override void WriteInt32(int int32) => writer.Write(int32);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override void WriteInt64(long int64) => writer.Write(int64);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override void WriteInt16(short int16) => writer.Write(int16);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override void WriteSByte(sbyte @sbyte) => writer.Write(@sbyte);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override void WriteSingle(float single) => writer.Write(single);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override void WriteString(string value)
    {
        ReadOnlySpan<char> str = value.AsSpan();
        writer.Write(str.Length);
        writer.Write(str);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override void WriteUInt32(uint uint32) => writer.Write(uint32);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override void WriteUInt64(ulong uint64) => writer.Write(uint64);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override void WriteUInt16(ushort uint16) => writer.Write(uint16);

    public override void WriteLightUserData(LightUserData lightUserData)
    {
        throw new LuaRuntimeException(state, $"attempt to serialize unsupported object type: {LuaValueType.LightUserData}");
    }

    public override void WriteUserData(ILuaUserData userData)
    {
        throw new LuaRuntimeException(state, $"attempt to serialize unsupported object type: {LuaValueType.UserData}");
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override void WriteChars(ReadOnlySpan<char> value) => writer.Write(value);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override void Reset()
    {
        Debug.Assert(!isReading && !isWriting);

        stream.SetLength(0);
        stream.Position = 0;
        readPosition = 0;

        state = null;
        cache = null;
    }

    public override void Dispose()
    {
        if (disposed)
        {
            return;
        }
        stream.Dispose();
        reader.Dispose();
        writer.Dispose();
        disposed = true;
        GC.SuppressFinalize(this);
    }

    public override string ToString()
    {
        if (cache == null)
        {
            int offset = readPosition;
            ReadOnlySpan<byte> span = stream.GetBuffer().AsSpan(offset, (int)stream.Length - offset);
            cache = Encoding.UTF8.GetString(span);
        }
        return cache;
    }
}

public abstract class StringBufferLibrary<TStringBuffer>
    where TStringBuffer : StringBuffer, new()
{
    private readonly struct Options
    {
        public const int DEFAULT_MAX_RECURSIONS = 32;

        public readonly int maxRecursions;

        public readonly ReadOnlyDictionary<string, ushort>? stringKey2Index;

        public readonly string[]? stringIndex2Key;

        public readonly ReadOnlyDictionary<LuaTable, ushort>? metatableKey2Index;

        public readonly LuaTable?[]? metatableIndex2Key;

        public Options()
        {
            maxRecursions = DEFAULT_MAX_RECURSIONS;
        }

        public Options(int maxRecursions, LuaTable? dictTable, LuaTable? metatableTable)
        {
            this.maxRecursions = maxRecursions;

            if (dictTable is not null)
            {
                int length = Math.Min(dictTable.ArrayLength, ushort.MaxValue - 1);
                Dictionary<string, ushort> key2index = new(length);
                stringIndex2Key = new string[length];

                for (ushort index = 0; index < length; ++index)
                {
                    if (!dictTable[index + 1].TryReadString(out string key))
                    {
                        break;
                    }
                    key2index[key] = index;
                    stringIndex2Key[index] = key;
                }

                stringKey2Index = new ReadOnlyDictionary<string, ushort>(key2index);
            }
            else
            {
                stringKey2Index = null;
                stringIndex2Key = null;
            }

            if (metatableTable is not null)
            {
                int length = Math.Min(metatableTable.ArrayLength, ushort.MaxValue - 1);
                Dictionary<LuaTable, ushort> key2index = new(length);
                metatableIndex2Key = new LuaTable[length];

                for (ushort index = 0; index < length; ++index)
                {
                    if (!metatableTable[index + 1].TryReadTable(out LuaTable metatable))
                    {
                        break;
                    }
                    key2index[metatable] = index;
                    metatableIndex2Key[index] = metatable;
                }

                metatableKey2Index = new ReadOnlyDictionary<LuaTable, ushort>(key2index);
            }
            else
            {
                metatableKey2Index = null;
                metatableIndex2Key = null;
            }
        }
    }

    private readonly struct SerialContext(LuaFunctionExecutionContext LuaContext, BufferObjectData data, int depth = 0)
    {
        public readonly LuaFunctionExecutionContext lua = LuaContext;
        public readonly BufferObjectData data = data;
        public readonly int depth = depth;

        public readonly SerialContext NextDepth()
        {
            return new SerialContext(lua, data, depth + 1);
        }
    }

    private class BufferObjectData
    {
        public const int DEFAULT_SIZE = 32;

        public required Options options;

        private TStringBuffer? stringBuffer;

        public TStringBuffer StringBuffer
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => stringBuffer ??= new TStringBuffer();
        }

        public BufferObjectData(int size = DEFAULT_SIZE)
        {
            stringBuffer = new TStringBuffer
            {
                Capacity = size
            };
        }

        ~BufferObjectData()
        {
            Free();
        }

        public string BufferToString(LuaFunctionExecutionContext context)
        {
            try
            {
                return StringBuffer.ToString() ?? string.Empty;
            }
            catch (Exception e)
            {
                throw new LuaRuntimeException(context.State, e);
            }
        }

        public void Free()
        {
            stringBuffer?.Dispose();
            stringBuffer = null;
        }
    }

    internal const string LIBRARY_NAME = "string.buffer";

    private const byte TOKEN_NIL = (byte)LuaValueType.Nil;
    private const byte TOKEN_BOOLEAN = (byte)LuaValueType.Boolean;
    private const byte TOKEN_STRING = (byte)LuaValueType.String;
    private const byte TOKEN_NUMBER = (byte)LuaValueType.Number;
    private const byte TOKEN_FUNCTION = (byte)LuaValueType.Function;
    private const byte TOKEN_THREAD = (byte)LuaValueType.Thread;
    private const byte TOKEN_LIGHT_USER_DATA = (byte)LuaValueType.LightUserData;
    private const byte TOKEN_USER_DATA = (byte)LuaValueType.UserData;
    private const byte TOKEN_TABLE = (byte)LuaValueType.Table;

    private const string BUFFER = "string.buffer";
    private const string BUFFER_ENCODE = $"{BUFFER}.encode";
    private const string BUFFER_DECODE = $"{BUFFER}.decode";
    private const string BUFFER_NEW = $"{BUFFER}.new";
    private const string BUFFER_OBJECT = $"{BUFFER}.object";
    private const string BUFFER_OBJECT_ENCODE = $"{BUFFER_OBJECT}.encode";
    private const string BUFFER_OBJECT_DECODE = $"{BUFFER_OBJECT}.decode";
    private const string BUFFER_OBJECT_RESET = $"{BUFFER_OBJECT}.reset";
    private const string BUFFER_OBJECT_FREE = $"{BUFFER_OBJECT}.free";
    private const string BUFFER_OBJECT_PUT = $"{BUFFER_OBJECT}.put";
    private const string BUFFER_OBJECT_PUTF = $"{BUFFER_OBJECT}.putf";
    private const string BUFFER_OBJECT_SET = $"{BUFFER_OBJECT}.set";
    private const string BUFFER_OBJECT_SKIP = $"{BUFFER_OBJECT}.skip";
    private const string BUFFER_OBJECT_GET = $"{BUFFER_OBJECT}.get";
    private const string BUFFER_OBJECT_TOSTRING = $"{BUFFER_OBJECT}.tostring";
    private const string BUFFER_OBJECT_LENGTH = $"{BUFFER_OBJECT}.length";
    private const string BUFFER_OBJECT_METATABLE = $"{BUFFER_OBJECT}.<metatable>";
    private const string BUFFER_OBJECT_METATABLE_NEWINDEX = $"{BUFFER_OBJECT_METATABLE}.__newindex";
    private const string BUFFER_OBJECT_METATABLE_LEN = $"{BUFFER_OBJECT_METATABLE}.__len";
    private const string BUFFER_OBJECT_METATABLE_TOSTRING = $"{BUFFER_OBJECT_METATABLE}.__tostring";
    private const string BUFFER_OBJECT_METATABLE_CONCAT = $"{BUFFER_OBJECT_METATABLE}.__concat";

    private static readonly ConditionalWeakTable<LuaUserData, BufferObjectData> bufferObjectDataTable = [];

    internal readonly LibraryFunction[] Functions;

    private readonly LuaTable bufferObjectMetatable;

    private readonly BufferObjectData defaultBufferObjectData = new()
    {
        options = new Options(),
    };

    /// <summary>
    /// Create the string buffer library.
    /// </summary>
    public StringBufferLibrary()
    {
        Functions =
        [
            new LibraryFunction(LIBRARY_NAME, "new", New),
            new LibraryFunction(LIBRARY_NAME, "encode", Encode),
            new LibraryFunction(LIBRARY_NAME, "decode", Decode),
        ];

        bufferObjectMetatable = new LuaTable(0, 10);
        bufferObjectMetatable["__index"] = new LuaTable()
        {
            Dictionary =
            {
                ["encode"] = GenerateEncodeFunction(),
                ["decode"] = GenerateDecodeFunction(),
                ["reset"] = GenerateResetFunction(),
                ["free"] = GenerateFreeFunction(),
                ["put"] = GeneratePutFunction(),
                ["putf"] = GeneratePutFFunction(),
                ["set"] = GenerateSetFunction(),
                ["skip"] = GenerateSkipFunction(),
                ["get"] = GenerateGetFunction(),
                ["tostring"] = GenerateToStringFunction(),
                // extended
                ["length"] = GenerateLengthFunction(),
            }
        };
        bufferObjectMetatable[Metamethods.NewIndex] = GenerateMetatableNewIndexFunction();
        bufferObjectMetatable[Metamethods.Len] = GenerateMetatableLenFunction();
        bufferObjectMetatable[Metamethods.ToString] = GenerateMetatableToStringFunction();
        bufferObjectMetatable[Metamethods.Concat] = GenerateMetatableConcatFunction();
    }

    private ValueTask<int> New(LuaFunctionExecutionContext context, CancellationToken cancellationToken)
    {
        int size = New_GetArgSize(context);
        Options options = New_GetArgOptions(context);

        LuaUserData bufferObject = new(LuaValue.Nil, bufferObjectMetatable);

        bufferObjectDataTable.AddOrUpdate(bufferObject, new BufferObjectData(size)
        {
            options = options,
        });

        return new(context.Return(new LuaValue(bufferObject)));
    }

    private static int New_GetArgSize(LuaFunctionExecutionContext context)
    {
        if (context.ArgumentCount >= 1)
        {
            LuaValue arg1 = context.Arguments[0];
            if (arg1.TryReadDouble(out double doubleValue))
            {
                return (int)doubleValue;
            }
            else if (arg1.Type != LuaValueType.Nil)
            {
                throw new LuaRuntimeException(context.State, $"bad argument #1 to 'size': number or nil expected, got {arg1.TypeToString()}");
            }
        }
        return BufferObjectData.DEFAULT_SIZE;
    }

    private Options New_GetArgOptions(LuaFunctionExecutionContext context)
    {
        Options options = defaultBufferObjectData.options;
        if (context.ArgumentCount >= 2)
        {
            LuaValue arg2 = context.Arguments[1];
            if (arg2.TryReadTable(out LuaTable table))
            {
                int maxRecursions = table.TryGetValue(nameof(maxRecursions), out LuaValue v1) && v1.TryReadNumber(out double v11) ? (int)v11 : options.maxRecursions;
                LuaTable? dict = table.TryGetValue(nameof(dict), out LuaValue v2) && v2.TryReadTable(out LuaTable v22) ? v22 : null;
                LuaTable? metatable = table.TryGetValue(nameof(metatable), out LuaValue v3) && v3.TryReadTable(out LuaTable v33) ? v33 : null;
                options = new Options(maxRecursions, dict, metatable);
            }
            else if (arg2.Type != LuaValueType.Nil)
            {
                throw new LuaRuntimeException(context.State, $"bad argument #2 to 'options': number or nil expected, got {arg2.TypeToString()}");
            }
        }
        return options;
    }

    private ValueTask<int> Encode(LuaFunctionExecutionContext context, CancellationToken cancellationToken)
    {
        EncodeImplement(context, defaultBufferObjectData, false);
        string result = defaultBufferObjectData.BufferToString(context);
        return new(context.Return(result));
    }

    private ValueTask<int> Decode(LuaFunctionExecutionContext context, CancellationToken cancellationToken)
    {
        return new(DecodeImplement(context, defaultBufferObjectData, true));
    }

    private LuaFunction GenerateEncodeFunction()
    {
        ValueTask<int> Encode(LuaFunctionExecutionContext context, CancellationToken cancellationToken)
        {
            (LuaUserData self, BufferObjectData data) = StringBufferObjectFetchSelf(context, BUFFER_OBJECT_ENCODE);
            EncodeImplement(context, data, true);
            LuaValue @return = new(self);
            return new(context.Return(@return));
        }
        return new LuaFunction(BUFFER_OBJECT_ENCODE, Encode);
    }

    private LuaFunction GenerateDecodeFunction()
    {
        ValueTask<int> Decode(LuaFunctionExecutionContext context, CancellationToken cancellationToken)
        {
            (LuaUserData self, BufferObjectData data) = StringBufferObjectFetchSelf(context, BUFFER_OBJECT_DECODE);
            return new(DecodeImplement(context, data, false));
        }
        return new LuaFunction(BUFFER_OBJECT_DECODE, Decode);
    }

    private static LuaFunction GenerateResetFunction()
    {
        static ValueTask<int> Reset(LuaFunctionExecutionContext context, CancellationToken cancellationToken)
        {
            (LuaUserData self, BufferObjectData data) = StringBufferObjectFetchSelf(context, BUFFER_OBJECT_RESET);
            data.StringBuffer.Reset();
            return new(context.Return(new LuaValue(self)));
        }
        return new LuaFunction(BUFFER_OBJECT_RESET, Reset);
    }

    private static LuaFunction GenerateFreeFunction()
    {
        static ValueTask<int> Free(LuaFunctionExecutionContext context, CancellationToken cancellationToken)
        {
            (LuaUserData self, BufferObjectData data) = StringBufferObjectFetchSelf(context, BUFFER_OBJECT_FREE);
            data.Free();
            return new(context.Return(new LuaValue(self)));
        }
        return new LuaFunction(BUFFER_OBJECT_FREE, Free);
    }

    private static LuaFunction GeneratePutFunction()
    {
        static ValueTask<int> Put(LuaFunctionExecutionContext context, CancellationToken cancellationToken)
        {
            (LuaUserData self, BufferObjectData data) = StringBufferObjectFetchSelf(context, BUFFER_OBJECT_PUT);
            TStringBuffer buffer = data.StringBuffer;
            buffer.BeginWrite(context.State);
            for (int index = 1; index < context.ArgumentCount; ++index)
            {
                BufferObjectPutValue(context, data, index);
            }
            buffer.EndWrite();
            return new(context.Return(new LuaValue(self)));
        }
        return new LuaFunction(BUFFER_OBJECT_PUT, Put);
    }

    private LuaFunction GeneratePutFFunction()
    {
        LuaValue formatFunction = new LuaFunction("format", StringLibrary.Instance.Format);
        // cache arguments to reduce gc
        LuaValue[] arguments = [];

        async ValueTask<int> PutF(LuaFunctionExecutionContext context, CancellationToken cancellationToken)
        {
            (LuaUserData self, BufferObjectData data) = StringBufferObjectFetchSelf(context, BUFFER_OBJECT_PUTF);
            if (arguments.Length < context.ArgumentCount - 1)
            {
                arguments = new LuaValue[context.ArgumentCount - 1];
            }
            for (int index = 1; index < context.ArgumentCount; ++index)
            {
                arguments[index - 1] = context.Arguments[index];
            }

            LuaValue[] results = await context.State.CallAsync(formatFunction, arguments.AsSpan(), cancellationToken);
            TStringBuffer buffer = data.StringBuffer;
            buffer.BeginWrite(context.State);
            try
            {
                buffer.WriteChars(results[0].UnsafeReadString().AsSpan());
            }
            finally
            {
                for (int index = 0; index < context.ArgumentCount - 1; ++index)
                {
                    arguments[index] = LuaValue.Nil;
                }
                buffer.EndWrite();
            }
            return context.Return(new LuaValue(self));
        }
        return new LuaFunction(BUFFER_OBJECT_PUTF, PutF);
    }

    private LuaFunction GenerateSetFunction()
    {
        string GetArgStr(LuaFunctionExecutionContext context)
        {
            if (!context.HasArgument(1))
            {
                throw new LuaRuntimeException(context.State, $"bad argument #2 to '{BUFFER_OBJECT_SET}': string expected, got nil");
            }
            LuaValue value = context.Arguments[1];
            if (!value.TryReadString(out string? str))
            {
                throw new LuaRuntimeException(context.State, $"bad argument #2 to '{BUFFER_OBJECT_SET}': string expected, got {value.TypeToString()}");
            }
            return str;
        }

        ValueTask<int> Set(LuaFunctionExecutionContext context, CancellationToken cancellationToken)
        {
            (LuaUserData self, BufferObjectData data) = StringBufferObjectFetchSelf(context, BUFFER_OBJECT_SET);
            string str = GetArgStr(context);
            StringBuffer buffer = data.StringBuffer;
            buffer.Reset();
            buffer.BeginWrite(context.State);
            buffer.WriteChars(str.AsSpan());
            buffer.EndWrite();
            LuaValue @return = new(self);
            return new(context.Return(@return));
        }
        return new LuaFunction(BUFFER_OBJECT_SET, Set);
    }

    private LuaFunction GenerateSkipFunction()
    {
        int GetArgLen(LuaFunctionExecutionContext context)
        {
            if (context.ArgumentCount < 2)
            {
                throw new LuaRuntimeException(context.State, $"bad argument #2 to '{BUFFER_OBJECT_SKIP}': string expected, got nil");
            }
            LuaValue value = context.Arguments[1];
            if (!value.TryReadDouble(out double number))
            {
                throw new LuaRuntimeException(context.State, $"bad argument #2 to '{BUFFER_OBJECT_SKIP}': string expected, got {value.TypeToString()}");
            }
            return (int)number;
        }

        ValueTask<int> Skip(LuaFunctionExecutionContext context, CancellationToken cancellationToken)
        {
            (LuaUserData self, BufferObjectData data) = StringBufferObjectFetchSelf(context, BUFFER_OBJECT_SKIP);
            int len = GetArgLen(context);
            data.StringBuffer.SkipRead(len);
            LuaValue @return = new(self);
            return new(context.Return(@return));
        }
        return new LuaFunction(BUFFER_OBJECT_SKIP, Skip);
    }

    private LuaFunction GenerateGetFunction()
    {
        LuaValue[] results = [];

        ValueTask<int> Get(LuaFunctionExecutionContext context, CancellationToken cancellationToken)
        {
            (LuaUserData self, BufferObjectData data) = StringBufferObjectFetchSelf(context, BUFFER_OBJECT_GET);
            int argumentCount = Math.Max(2, context.ArgumentCount);
            if (results.Length < argumentCount - 1)
            {
                results = new LuaValue[argumentCount - 1];
            }

            TStringBuffer buffer = data.StringBuffer;
            buffer.BeginRead(context.State);
            for (int index = 1; index < argumentCount; ++index)
            {
                results[index - 1] = BufferObjectGetValue(context, data, index);
            }
            buffer.EndRead();

            int @return = context.Return(results);
            for (int index = 0; index < results.Length; ++index)
            {
                results[index] = LuaValue.Nil;
            }
            return new(@return);
        }
        return new LuaFunction(BUFFER_OBJECT_GET, Get);
    }

    private static LuaFunction GenerateToStringFunction(string functionName = BUFFER_OBJECT_TOSTRING)
    {
        static ValueTask<int> ToString(LuaFunctionExecutionContext context, CancellationToken cancellationToken)
        {
            (LuaUserData self, BufferObjectData data) = StringBufferObjectFetchSelf(context, BUFFER_OBJECT_TOSTRING);
            return new(context.Return(data.BufferToString(context)));
        }
        return new LuaFunction(BUFFER_OBJECT_TOSTRING, ToString);
    }

    private static LuaFunction GenerateLengthFunction(string functionName = BUFFER_OBJECT_LENGTH)
    {
        ValueTask<int> ToString(LuaFunctionExecutionContext context, CancellationToken cancellationToken)
        {
            (LuaUserData self, BufferObjectData data) = StringBufferObjectFetchSelf(context, functionName);
            return new(context.Return(data.StringBuffer.Length));
        }
        return new LuaFunction(functionName, ToString);
    }

    private static LuaFunction GenerateMetatableNewIndexFunction()
    {
        static ValueTask<int> MetatableLength(LuaFunctionExecutionContext context, CancellationToken cancellationToken)
        {
            throw new LuaRuntimeException(context.State, $"cannot modify userdata {BUFFER_OBJECT}");
        }
        return new LuaFunction(BUFFER_OBJECT_METATABLE_NEWINDEX, MetatableLength);
    }

    private static LuaFunction GenerateMetatableLenFunction() => GenerateLengthFunction(BUFFER_OBJECT_METATABLE_LEN);

    private static LuaFunction GenerateMetatableToStringFunction() => GenerateToStringFunction(BUFFER_OBJECT_METATABLE_TOSTRING);

    private LuaFunction GenerateMetatableConcatFunction()
    {
        StringBuilder builder = new();

        void ConcatString(LuaFunctionExecutionContext context, int index)
        {
            LuaValue value = context.Arguments[index];
            switch (value.Type)
            {
                case LuaValueType.String:
                    builder.Append(value.UnsafeReadString());
                    break;
                case LuaValueType.Number:
                    builder.Append(value.UnsafeReadDouble());
                    break;
                case LuaValueType.UserData:
                    if (!value.TryRead(out LuaUserData userData) || !bufferObjectDataTable.TryGetValue(userData, out BufferObjectData? otherData))
                    {
                        throw new LuaRuntimeException(context.State, $"bad argument #{index + 1} to '{BUFFER_OBJECT_METATABLE_CONCAT}': string/number/{BUFFER_OBJECT} expected, got an invalid userdata");
                    }
                    builder.Append(otherData.BufferToString(context));
                    break;
                default:
                    throw new LuaRuntimeException(context.State, $"bad argument #{index + 1} to '{BUFFER_OBJECT_METATABLE_CONCAT}': string/number/{BUFFER_OBJECT} expected, got {value.TypeToString()}");
            }
        }

        ValueTask<int> MetatableConcat(LuaFunctionExecutionContext context, CancellationToken cancellationToken)
        {
            try
            {
                if (context.ArgumentCount == 0)
                {
                    throw new LuaRuntimeException(context.State, $"bad argument #1 to '{BUFFER_OBJECT_METATABLE_CONCAT}': userdata expected, got nil");
                }
                for (int index = 0; index < context.ArgumentCount; ++index)
                {
                    ConcatString(context, index);
                }
                string result = builder.ToString();
                return new(context.Return(result));
            }
            finally
            {
                builder.Clear();
            }
        }
        return new LuaFunction(BUFFER_OBJECT_METATABLE_CONCAT, MetatableConcat);
    }

    private static (LuaUserData self, BufferObjectData data) StringBufferObjectFetchSelf(LuaFunctionExecutionContext context, string functionName)
    {
        if (context.ArgumentCount == 0)
        {
            throw new LuaRuntimeException(context.State, $"bad argument #1 to '{functionName}': userdata expected, got nil");
        }
        if (!context.Arguments[0].TryRead(out LuaUserData bufferObject))
        {
            throw new LuaRuntimeException(context.State, $"bad argument #1 to '{functionName}': userdata expected, got {context.Arguments[0].TypeToString()}");
        }
        if (!bufferObjectDataTable.TryGetValue(bufferObject, out BufferObjectData? data))
        {
            throw new LuaRuntimeException(context.State, $"bad argument #1 to '{functionName}': userdata is not a string buffer object");
        }
        return (bufferObject, data);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void EncodeImplement(LuaFunctionExecutionContext context, BufferObjectData data, bool useSecondParam)
    {
        LuaValue value = context.ArgumentCount < 1 ? LuaValue.Nil : context.Arguments[useSecondParam ? 1 : 0];
        SerialContext serialContext = new(context, data);

        TStringBuffer buffer = data.StringBuffer;
        buffer.BeginWrite(context.State);
        try
        {
            WriteLuaValue(value, serialContext);
        }
        finally
        {
            buffer.EndWrite();
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int DecodeImplement(LuaFunctionExecutionContext context, BufferObjectData data, bool useSecondParam)
    {
        LuaValue inStr = context.ArgumentCount >= 2 ? context.Arguments[1] : LuaValue.Nil;
        SerialContext serialContext = new(context, data);
        TStringBuffer buffer = data.StringBuffer;

        if (useSecondParam && inStr.TryReadString(out string str))
        {
            buffer.BeginWrite(context.State);
            try
            {
                buffer.WriteString(str);
            }
            finally
            {
                buffer.EndWrite();
            }
        }

        buffer.BeginRead(context.State);
        try
        {
            LuaValue @return = ReadLuaValue(serialContext);
            return context.Return(@return);
        }
        finally
        {
            buffer.EndRead();
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void WriteLuaValue(LuaValue value, SerialContext serialContext)
    {
        TStringBuffer buffer = serialContext.data.StringBuffer;
        buffer.WriteByte((byte)value.Type);
        switch (value.Type)
        {
            case LuaValueType.Nil:
                break;
            case LuaValueType.Boolean:
                buffer.WriteBoolean(value.UnsafeReadDouble() != 0);
                break;
            case LuaValueType.String:
                buffer.WriteString(value.UnsafeReadString());
                break;
            case LuaValueType.Number:
                buffer.WriteDouble(value.UnsafeReadDouble());
                break;
            case LuaValueType.Function:
                WriteFunction(serialContext);
                break;
            case LuaValueType.Thread:
                WriteThread(serialContext);
                break;
            case LuaValueType.LightUserData:
                WriteLightUserData(serialContext, (value.UnsafeReadDouble(), value.UnsafeReadObject()));
                break;
            case LuaValueType.UserData:
                WriteUserData(serialContext, value.UnsafeRead<ILuaUserData>());
                break;
            case LuaValueType.Table:
                WriteTable(serialContext.NextDepth(), value.Read<LuaTable>());
                break;
            default:
                break;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void WriteFunction(SerialContext serialContext)
    {
        throw new LuaRuntimeException(serialContext.lua.State, $"attempt to serialize unsupported object type: {LuaValueType.Function}");
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void WriteThread(SerialContext serialContext)
    {
        throw new LuaRuntimeException(serialContext.lua.State, $"attempt to serialize unsupported object type: {LuaValueType.Thread}");
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void WriteLightUserData(SerialContext serialContext, LightUserData lightUserData)
    {
        TStringBuffer buffer = serialContext.data.StringBuffer;
        buffer.WriteLightUserData(lightUserData);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void WriteUserData(SerialContext serialContext, ILuaUserData userData)
    {
        TStringBuffer buffer = serialContext.data.StringBuffer;
        buffer.WriteUserData(userData);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void WriteTable(SerialContext serialContext, LuaTable luaTable)
    {
        if (serialContext.depth >= serialContext.data.options.maxRecursions)
        {
            throw DeeplyNestedTableException(serialContext.lua.State);
        }

        if (serialContext.data.options.stringKey2Index is not null)
        {
            if (serialContext.data.options.metatableKey2Index is not null)
            {
                WriteTableWithDict(serialContext, luaTable);
                WriteTableResolveMetatable(serialContext, luaTable);
            }
            else
            {
                WriteTableWithDict(serialContext, luaTable);
            }
        }
        else
        {
            if (serialContext.data.options.metatableKey2Index is not null)
            {
                WriteTableWithoutDict(serialContext, luaTable);
                WriteTableResolveMetatable(serialContext, luaTable);
            }
            else
            {
                WriteTableWithoutDict(serialContext, luaTable);
            }
        }
    }

    private static void WriteTableWithDict(SerialContext serialContext, LuaTable luaTable)
    {
        TStringBuffer buffer = serialContext.data.StringBuffer;
        buffer.WriteInt32(luaTable.ArrayLength);
        buffer.WriteInt32(luaTable.HashMapCount);
        foreach ((LuaValue key, LuaValue value) in luaTable)
        {
            WriteTableEntryWithDict(serialContext, key);
            WriteTableEntryWithDict(serialContext, value);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void WriteTableEntryWithDict(SerialContext serialContext, LuaValue value)
    {
        if (value.Type == LuaValueType.String && serialContext.data.options.stringKey2Index!.TryGetValue(value.UnsafeReadString(), out ushort dictIndex))
        {
            serialContext.data.StringBuffer.WriteUInt16((ushort)(dictIndex + 1));
        }
        else
        {
            serialContext.data.StringBuffer.WriteUInt16(0);
            WriteLuaValue(value, serialContext);
        }
    }

    private static void WriteTableWithoutDict(SerialContext serialContext, LuaTable luaTable)
    {
        TStringBuffer buffer = serialContext.data.StringBuffer;
        buffer.WriteInt32(luaTable.ArrayLength);
        buffer.WriteInt32(luaTable.HashMapCount);
        foreach ((LuaValue key, LuaValue value) in luaTable)
        {
            WriteLuaValue(key, serialContext);
            WriteLuaValue(value, serialContext);
        }
    }

    private static void WriteTableResolveMetatable(SerialContext serialContext, LuaTable luaTable)
    {
        ReadOnlyDictionary<LuaTable, ushort> key2index = serialContext.data.options.metatableKey2Index!;
        TStringBuffer buffer = serialContext.data.StringBuffer;
        if (luaTable.Metatable is not null && key2index.TryGetValue(luaTable.Metatable, out ushort metatableIndex))
        {
            buffer.WriteUInt16((ushort)(metatableIndex + 1));
        }
        else
        {
            buffer.WriteUInt16(0);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static LuaValue ReadLuaValue(SerialContext serialContext)
    {
        if (serialContext.depth >= serialContext.data.options.maxRecursions)
        {
            throw DeeplyNestedTableException(serialContext.lua.State);
        }

        TStringBuffer reader = serialContext.data.StringBuffer;
        byte token = reader.ReadByte();
        return token switch
        {
            TOKEN_NIL => LuaValue.Nil,
            TOKEN_BOOLEAN => (LuaValue)reader.ReadBoolean(),
            TOKEN_STRING => ReadString(serialContext),
            TOKEN_NUMBER => (LuaValue)reader.ReadDouble(),
            TOKEN_FUNCTION => ReadFunction(serialContext),
            TOKEN_THREAD => ReadThread(serialContext),
            TOKEN_LIGHT_USER_DATA => ReadLightUserData(serialContext),
            TOKEN_USER_DATA => ReadUserData(serialContext),
            TOKEN_TABLE => ReadTable(serialContext.NextDepth()),
            _ => throw new LuaRuntimeException(serialContext.lua.State, $"invalid token detected: {token}"),
        };
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static LuaValue ReadString(SerialContext serialContext)
    {
        StringBuffer buffer = serialContext.data.StringBuffer;
        int length = buffer.ReadInt32();
        return buffer.ReadString(length);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static LuaValue ReadFunction(SerialContext serialContext)
    {
        throw new LuaRuntimeException(serialContext.lua.State, $"attempt to deserialize unsupported object type: {LuaValueType.Function}");
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static LuaValue ReadThread(SerialContext serialContext)
    {
        throw new LuaRuntimeException(serialContext.lua.State, $"attempt to deserialize unsupported object type: {LuaValueType.Thread}");
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static LuaValue ReadLightUserData(SerialContext serialContext)
    {
        StringBuffer buffer = serialContext.data.StringBuffer;
        (double value, object? referenceValue) = buffer.ReadLightUserData();
        return new LuaValue(LuaValueType.LightUserData, value, referenceValue);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static LuaValue ReadUserData(SerialContext serialContext)
    {
        StringBuffer buffer = serialContext.data.StringBuffer;
        return new LuaValue(buffer.ReadUserData());
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static LuaTable ReadTable(SerialContext serialContext)
    {
        if (serialContext.data.options.stringKey2Index is not null)
        {
            if (serialContext.data.options.metatableKey2Index is not null)
            {
                return ReadTableSolveMetatable(serialContext, ReadTableWithDict(serialContext));
            }
            else
            {
                return ReadTableWithDict(serialContext);
            }
        }
        else
        {
            if (serialContext.data.options.metatableKey2Index is not null)
            {
                return ReadTableSolveMetatable(serialContext, ReadTableWithoutDict(serialContext));
            }
            else
            {
                return ReadTableWithoutDict(serialContext);
            }
        }
    }

    private static LuaTable ReadTableWithDict(SerialContext serialContext)
    {
        StringBuffer buffer = serialContext.data.StringBuffer;
        int arrayLength = buffer.ReadInt32();
        int hashMapCount = buffer.ReadInt32();
        LuaTable table = new(arrayLength, hashMapCount);
        for (int _ = 0; _ < arrayLength + hashMapCount; ++_)
        {
            LuaValue key = ReadLuaTableEntryWithDict(serialContext);
            LuaValue value = ReadLuaTableEntryWithDict(serialContext);
            table[key] = value;
        }
        return table;
    }

    private static LuaValue ReadLuaTableEntryWithDict(SerialContext serialContext)
    {
        ushort dictIndex = serialContext.data.StringBuffer.ReadUInt16();
        if (dictIndex != 0)
        {
            return serialContext.data.options.stringIndex2Key![dictIndex - 1];
        }
        else
        {
            return ReadLuaValue(serialContext);
        }
    }

    private static LuaTable ReadTableWithoutDict(SerialContext serialContext)
    {
        StringBuffer buffer = serialContext.data.StringBuffer;
        int arrayLength = buffer.ReadInt32();
        int hashMapCount = buffer.ReadInt32();
        LuaTable table = new(arrayLength, hashMapCount);
        for (int _ = 0; _ < arrayLength + hashMapCount; ++_)
        {
            LuaValue key = ReadLuaValue(serialContext);
            LuaValue value = ReadLuaValue(serialContext);
            table[key] = value;
        }
        return table;
    }

    private static LuaTable ReadTableSolveMetatable(SerialContext serialContext, LuaTable luaTable)
    {
        StringBuffer buffer = serialContext.data.StringBuffer;
        LuaTable[] index2key = serialContext.data.options.metatableIndex2Key!;
        ushort metatableIndex = buffer.ReadUInt16();
        if (metatableIndex != 0)
        {
            luaTable.Metatable = index2key[metatableIndex - 1];
        }
        return luaTable;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private async static void BufferObjectPutValue(LuaFunctionExecutionContext context, BufferObjectData data, int index)
    {
        StringBuffer buffer = data.StringBuffer;
        LuaValue value = context.Arguments[index];
        switch (value.Type)
        {
            case LuaValueType.String:
                buffer.WriteChars(value.UnsafeReadString().AsSpan());
                break;
            case LuaValueType.Number:
                buffer.WriteDouble(value.UnsafeReadDouble());
                break;
            case LuaValueType.Table:
                {
                    string? str = await StringBufferLibraryUtility.LuaTableMetatableString(context.State, value.UnsafeRead<LuaTable>())
                        ?? throw new LuaRuntimeException(context.State, $"bad argument #2 to '{BUFFER_OBJECT_PUT}': table require meta-method '__tostring'");
                    buffer.WriteChars(str.AsSpan());
                }
                break;
            default:
                throw new LuaRuntimeException(context.State, $"bad argument #2 to '{BUFFER_OBJECT_PUT}': string,number/table expected, got {value.TypeToString()}");
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static LuaValue BufferObjectGetValue(LuaFunctionExecutionContext context, BufferObjectData data, int index)
    {
        LuaValue value = index < context.ArgumentCount ? context.Arguments[index] : LuaValue.Nil;
        if (value.Type == LuaValueType.Nil)
        {
            return data.StringBuffer.ReadString(data.StringBuffer.Length);
        }
        if (value.TryReadNumber(out double length))
        {
            return data.StringBuffer.ReadString((int)length);
        }
        throw new LuaRuntimeException(context.State, $"bad argument #{index + 1} to '{BUFFER_OBJECT_GET}': number/nil expected, got {value.TypeToString()}");
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static LuaRuntimeException DeeplyNestedTableException(LuaState luaState)
    {
        return new LuaRuntimeException(luaState, "failed to serialize deeply nested table");
    }
}

/// <summary>
/// The default string buffer library.
/// This is sufficient usually, unless you want to provide serialization and deserialization for your own UserData classes.
/// <see cref="BasicStringBuffer.ReadUserData"/>
/// <see cref="BasicStringBuffer.ReadLightUserData"/>
/// <see cref="BasicStringBuffer.WriteUserData(ILuaUserData)"/>
/// <see cref="BasicStringBuffer.WriteLightUserData(LightUserData)"/>
/// </summary>
public sealed class StringBufferLibrary : StringBufferLibrary<BasicStringBuffer>
{
    public static readonly StringBufferLibrary Instance = new();
}

internal static class StringBufferLibraryUtility
{
    // cache arguments to reduce gc
    private static readonly LuaValue[] _LuaTableMetatableString_arguments = [LuaValue.Nil];

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static async Task<string?> LuaTableMetatableString(LuaState state, LuaTable luaTable)
    {
        if (luaTable.Metatable is not null && luaTable.Metatable.TryGetValue("__tostring", out LuaValue tostring))
        {
            _LuaTableMetatableString_arguments[0] = luaTable;
            LuaValue[] results = await state.CallAsync(tostring, _LuaTableMetatableString_arguments);
            if (results.Length >= 1)
            {
                return results[0].ToString();
            }
        }
        return null;
    }
}
