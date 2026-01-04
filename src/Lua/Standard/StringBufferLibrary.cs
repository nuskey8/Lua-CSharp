using System.Runtime.CompilerServices;
using System.Text;
using System.Diagnostics;
using System.Collections.ObjectModel;

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

    public abstract void Write(bool boolean);

    public abstract void Write(byte @byte);

    public abstract void Write(double @double);

    public abstract void Write(int int32);

    public abstract void Write(long int64);

    public abstract void Write(short int16);

    public abstract void Write(sbyte @sbyte);

    public abstract void Write(float single);

    public abstract void Write(string @string);

    public abstract void Write(uint uint32);

    public abstract void Write(ulong uint64);

    public abstract void Write(ushort uint16);

    public abstract void Write(LightUserData lightUserData);

    public abstract void Write(ILuaUserData userData);

    public abstract void Write(ReadOnlySpan<char> chars);

    public abstract void Reset();

    public abstract void Dispose();
}

/// <summary>
/// The basic string buffer object.
/// <para>
/// If you want to add your own serialization for userdata, override the following methods:
/// <see cref="ReadUserData"/>
/// <see cref="ReadLightUserData"/>
/// <see cref="Write(ILuaUserData)"/>
/// <see cref="Write(LightUserData)"/>
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
    public override void Write(bool boolean) => writer.Write(boolean);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override void Write(byte @byte) => writer.Write(@byte);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override void Write(double @double) => writer.Write(@double);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override void Write(int int32) => writer.Write(int32);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override void Write(long int64) => writer.Write(int64);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override void Write(short int16) => writer.Write(int16);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override void Write(sbyte @sbyte) => writer.Write(@sbyte);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override void Write(float single) => writer.Write(single);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override void Write(string value)
    {
        ReadOnlySpan<char> str = value.AsSpan();
        writer.Write(str.Length);
        writer.Write(str);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override void Write(uint uint32) => writer.Write(uint32);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override void Write(ulong uint64) => writer.Write(uint64);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override void Write(ushort uint16) => writer.Write(uint16);

    public override void Write(LightUserData lightUserData)
    {
        throw new LuaRuntimeException(state, $"attempt to serialize unsupported object type: {LuaValueType.LightUserData}");
    }

    public override void Write(ILuaUserData userData)
    {
        throw new LuaRuntimeException(state, $"attempt to serialize unsupported object type: {LuaValueType.UserData}");
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override void Write(ReadOnlySpan<char> value) => writer.Write(value);

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
    private struct Options
    {
        public int maxRecursions;

        public readonly ReadOnlyDictionary<string, short>? dict;

        public readonly ReadOnlyDictionary<short, string>? dictInv;

        public Options(int maxRecursions, LuaTable? dictTable)
        {
            this.maxRecursions = maxRecursions;

            if (dictTable is not null)
            {
                Dictionary<string, short> dict = [];
                Dictionary<short, string> dictInv = [];

                for (short index = 0; index < Math.Min(dictTable.ArrayLength, short.MaxValue); ++index)
                {
                    if (dictTable[index].TryReadString(out string key))
                    {
                        dict[key] = index;
                        dictInv[index] = key;
                    }
                }

                this.dict = new ReadOnlyDictionary<string, short>(dict);
                this.dictInv = new ReadOnlyDictionary<short, string>(dictInv);
            }
            else
            {
                dict = null;
                dictInv = null;
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
        public required Options options;

        private TStringBuffer? stringBuffer;

        public TStringBuffer StringBuffer
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => stringBuffer ??= new TStringBuffer();
        }

        public BufferObjectData(int size)
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
    private const string BUFFER_OBJECT_METATABLE = $"{BUFFER_OBJECT}.<metatable>";
    private const string BUFFER_OBJECT_METATABLE_NEWINDEX = $"{BUFFER_OBJECT_METATABLE}.__newindex";
    private const string BUFFER_OBJECT_METATABLE_LEN = $"{BUFFER_OBJECT_METATABLE}.__len";
    private const string BUFFER_OBJECT_METATABLE_TOSTRING = $"{BUFFER_OBJECT_METATABLE}.__tostring";
    private const string BUFFER_OBJECT_METATABLE_CONCAT = $"{BUFFER_OBJECT_METATABLE}.__concat";

    private static readonly ConditionalWeakTable<LuaUserData, BufferObjectData> bufferObjectDataTable = [];

    internal readonly LibraryFunction[] Functions;

    private readonly LuaTable bufferObjectMetatable;

    private readonly BufferObjectData defaultBufferObjectData = new(32)
    {
        options = new Options(32, null),
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

        bufferObjectMetatable = new LuaTable(0, 2);
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
            }
        };
        bufferObjectMetatable["__newindex"] = GenerateMetatableNewIndexFunction();
        bufferObjectMetatable["__len"] = GenerateMetatableLenFunction();
        bufferObjectMetatable["__tostring"] = GenerateMetatableToStringFunction();
        bufferObjectMetatable["__concat"] = GenerateMetatableConcatFunction();
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

    private int New_GetArgSize(LuaFunctionExecutionContext context)
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
        return 32;
    }

    private Options New_GetArgOptions(LuaFunctionExecutionContext context)
    {
        Options options = defaultBufferObjectData.options;
        if (context.ArgumentCount >= 2)
        {
            LuaValue arg2 = context.Arguments[1];
            if (arg2.TryReadTable(out LuaTable table))
            {
                if (table.TryGetValue(nameof(options.maxRecursions), out LuaValue maxRecursions) && maxRecursions.TryReadNumber(out double doubleValue))
                {
                    options.maxRecursions = (int)doubleValue;
                }
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

    private LuaFunction GenerateResetFunction()
    {
        static ValueTask<int> Reset(LuaFunctionExecutionContext context, CancellationToken cancellationToken)
        {
            (LuaUserData self, BufferObjectData data) = StringBufferObjectFetchSelf(context, BUFFER_OBJECT_RESET);
            data.StringBuffer.Reset();
            return new(context.Return(new LuaValue(self)));
        }
        return new LuaFunction(BUFFER_OBJECT_RESET, Reset);
    }

    private LuaFunction GenerateFreeFunction()
    {
        static ValueTask<int> Free(LuaFunctionExecutionContext context, CancellationToken cancellationToken)
        {
            (LuaUserData self, BufferObjectData data) = StringBufferObjectFetchSelf(context, BUFFER_OBJECT_FREE);
            data.Free();
            return new(context.Return(new LuaValue(self)));
        }
        return new LuaFunction(BUFFER_OBJECT_FREE, Free);
    }

    private LuaFunction GeneratePutFunction()
    {
        ValueTask<int> Put(LuaFunctionExecutionContext context, CancellationToken cancellationToken)
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

        ValueTask<int> PutF(LuaFunctionExecutionContext context, CancellationToken cancellationToken)
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

            LuaValue[] results = context.State.CallAsync(formatFunction, arguments.AsSpan()).Result;
            TStringBuffer buffer = data.StringBuffer;
            buffer.BeginWrite(context.State);
            try
            {
                buffer.Write(results[0].UnsafeReadString().AsSpan());
            }
            finally
            {
                for (int index = 0; index < context.ArgumentCount - 1; ++index)
                {
                    arguments[index] = LuaValue.Nil;
                }
                buffer.EndWrite();
            }
            return new(context.Return(new LuaValue(self)));
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
            buffer.Write(str.AsSpan());
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

            int returns = context.Return(results);
            for (int index = 0; index < results.Length; ++index)
            {
                results[index] = LuaValue.Nil;
            }
            return new(returns);
        }
        return new LuaFunction(BUFFER_OBJECT_GET, Get);
    }

    private LuaFunction GenerateToStringFunction(string functionName = BUFFER_OBJECT_TOSTRING)
    {
        static ValueTask<int> ToString(LuaFunctionExecutionContext context, CancellationToken cancellationToken)
        {
            (LuaUserData self, BufferObjectData data) = StringBufferObjectFetchSelf(context, BUFFER_OBJECT_TOSTRING);
            return new(context.Return(data.BufferToString(context)));
        }
        return new LuaFunction(BUFFER_OBJECT_TOSTRING, ToString);
    }

    private LuaFunction GenerateMetatableNewIndexFunction()
    {
        static ValueTask<int> MetatableLength(LuaFunctionExecutionContext context, CancellationToken cancellationToken)
        {
            throw new LuaRuntimeException(context.State, $"cannot modify userdata {BUFFER_OBJECT}");
        }
        return new LuaFunction(BUFFER_OBJECT_METATABLE_NEWINDEX, MetatableLength);
    }

    private LuaFunction GenerateMetatableLenFunction()
    {
        static ValueTask<int> MetatableLen(LuaFunctionExecutionContext context, CancellationToken cancellationToken)
        {
            (LuaUserData self, BufferObjectData data) = StringBufferObjectFetchSelf(context, BUFFER_OBJECT_METATABLE_LEN);
            return new(context.Return(data.StringBuffer.Length));
        }
        return new LuaFunction(BUFFER_OBJECT_METATABLE_LEN, MetatableLen);
    }

    private LuaFunction GenerateMetatableToStringFunction() => GenerateToStringFunction(BUFFER_OBJECT_METATABLE_TOSTRING);

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
                    builder.Append(value.UnsafeReadDouble().ToString());
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
                buffer.Write(str);
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
    private void WriteLuaValue(LuaValue value, SerialContext serialContext)
    {
        TStringBuffer buffer = serialContext.data.StringBuffer;
        buffer.Write((byte)value.Type);
        switch (value.Type)
        {
            case LuaValueType.Nil:
                break;
            case LuaValueType.Boolean:
                buffer.Write(value.UnsafeReadDouble() != 0);
                break;
            case LuaValueType.String:
                buffer.Write(value.UnsafeReadString());
                break;
            case LuaValueType.Number:
                buffer.Write(value.UnsafeReadDouble());
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
    private void WriteFunction(SerialContext serialContext)
    {
        throw new LuaRuntimeException(serialContext.lua.State, $"attempt to serialize unsupported object type: {LuaValueType.Function}");
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void WriteThread(SerialContext serialContext)
    {
        throw new LuaRuntimeException(serialContext.lua.State, $"attempt to serialize unsupported object type: {LuaValueType.Thread}");
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void WriteLightUserData(SerialContext serialContext, LightUserData lightUserData)
    {
        TStringBuffer buffer = serialContext.data.StringBuffer;
        buffer.Write(lightUserData);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void WriteUserData(SerialContext serialContext, ILuaUserData userData)
    {
        TStringBuffer buffer = serialContext.data.StringBuffer;
        buffer.Write(userData);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void WriteTable(SerialContext serialContext, LuaTable luaTable)
    {
        if (serialContext.depth >= serialContext.data.options.maxRecursions)
        {
            throw DeeplyNestedTableException(serialContext.lua.State);
        }

        if (serialContext.data.options.dict is not null)
        {
            WriteTableWithDict(serialContext, luaTable);
        }
        else
        {
            WriteTableWithoutDict(serialContext, luaTable);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void WriteTableWithDict(SerialContext serialContext, LuaTable luaTable)
    {
        ReadOnlyDictionary<string, short>? dict = serialContext.data.options.dict!;
        TStringBuffer buffer = serialContext.data.StringBuffer;
        buffer.Write(luaTable.ArrayLength);
        buffer.Write(luaTable.HashMapCount);
        foreach ((LuaValue key, LuaValue value) in luaTable)
        {
            if (key.Type == LuaValueType.String && dict.TryGetValue(key.UnsafeReadString(), out short dictIndex))
            {
                Debug.Assert(dictIndex != 0);
                buffer.Write(dictIndex);
            }
            else
            {
                buffer.Write(0);
                WriteLuaValue(key, serialContext);
            }
            WriteLuaValue(value, serialContext);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void WriteTableWithoutDict(SerialContext serialContext, LuaTable luaTable)
    {
        TStringBuffer buffer = serialContext.data.StringBuffer;
        buffer.Write(luaTable.ArrayLength);
        buffer.Write(luaTable.HashMapCount);
        foreach ((LuaValue key, LuaValue value) in luaTable)
        {
            WriteLuaValue(key, serialContext);
            WriteLuaValue(value, serialContext);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private LuaValue ReadLuaValue(SerialContext serialContext)
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
    private LuaValue ReadString(SerialContext serialContext)
    {
        StringBuffer buffer = serialContext.data.StringBuffer;
        int length = buffer.ReadInt32();
        return buffer.ReadString(length);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private LuaValue ReadFunction(SerialContext serialContext)
    {
        throw new LuaRuntimeException(serialContext.lua.State, $"attempt to deserialize unsupported object type: {LuaValueType.Function}");
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private LuaValue ReadThread(SerialContext serialContext)
    {
        throw new LuaRuntimeException(serialContext.lua.State, $"attempt to deserialize unsupported object type: {LuaValueType.Thread}");
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private LuaValue ReadLightUserData(SerialContext serialContext)
    {
        StringBuffer buffer = serialContext.data.StringBuffer;
        (double value, object? referenceValue) = buffer.ReadLightUserData();
        return new LuaValue(LuaValueType.LightUserData, value, referenceValue);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private LuaValue ReadUserData(SerialContext serialContext)
    {
        StringBuffer buffer = serialContext.data.StringBuffer;
        return new LuaValue(buffer.ReadUserData());
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private LuaTable ReadTable(SerialContext serialContext)
    {
        if (serialContext.data.options.dict is not null)
        {
            return ReadTableWithDict(serialContext);
        }
        else
        {
            return ReadTableWithoutDict(serialContext);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private LuaTable ReadTableWithDict(SerialContext serialContext)
    {
        ReadOnlyDictionary<short, string> dictInv = serialContext.data.options.dictInv!;
        StringBuffer buffer = serialContext.data.StringBuffer;
        int arrayLength = buffer.ReadInt32();
        int hashMapCount = buffer.ReadInt32();
        LuaTable table = new(arrayLength, hashMapCount);
        for (int _ = 0; _ < arrayLength + hashMapCount; ++_)
        {
            short dictIndex = buffer.ReadInt16();
            LuaValue key = dictIndex != 0 ? dictInv[dictIndex] : ReadLuaValue(serialContext);
            LuaValue value = ReadLuaValue(serialContext);
            table[key] = value;
        }
        return table;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private LuaTable ReadTableWithoutDict(SerialContext serialContext)
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

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void BufferObjectPutValue(LuaFunctionExecutionContext context, BufferObjectData data, int index)
    {
        StringBuffer buffer = data.StringBuffer;
        LuaValue value = context.Arguments[index];
        switch (value.Type)
        {
            case LuaValueType.String:
                buffer.Write(value.UnsafeReadString().AsSpan());
                break;
            case LuaValueType.Number:
                buffer.Write(value.UnsafeReadDouble());
                break;
            case LuaValueType.Table:
                if (!StringBufferLibraryUtility.LuaTableMetatableString(context.State, value.UnsafeRead<LuaTable>(), out string str))
                {
                    throw new LuaRuntimeException(context.State, $"bad argument #2 to '{BUFFER_OBJECT_PUT}': table require meta-method '__tostring'");
                }
                buffer.Write(str.AsSpan());
                break;
            default:
                throw new LuaRuntimeException(context.State, $"bad argument #2 to '{BUFFER_OBJECT_PUT}': string,number/table expected, got {value.TypeToString()}");
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private LuaValue BufferObjectGetValue(LuaFunctionExecutionContext context, BufferObjectData data, int index)
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

    private Exception DeeplyNestedTableException(LuaState luaState)
    {
        return new LuaRuntimeException(luaState, "failed to serialize deeply nested table");
    }
}

/// <summary>
/// The default string buffer library.
/// This is sufficient usually, unless you want to provide serialization and deserialization for your own UserData classes.
/// <see cref="BasicStringBuffer.ReadUserData"/>
/// <see cref="BasicStringBuffer.ReadLightUserData"/>
/// <see cref="BasicStringBuffer.Write(ILuaUserData)"/>
/// <see cref="BasicStringBuffer.Write(LightUserData)"/>
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
    internal static bool LuaTableMetatableString(LuaState state, LuaTable luaTable, out string result)
    {
        if (luaTable.Metatable is not null && luaTable.Metatable.TryGetValue("__tostring", out LuaValue tostring))
        {
            _LuaTableMetatableString_arguments[0] = luaTable;
            LuaValue[] results = state.CallAsync(tostring, _LuaTableMetatableString_arguments).Result;
            if (results.Length >= 1)
            {
                result = results[0].ToString();
                return true;
            }
        }
        result = string.Empty;
        return false;
    }
}
