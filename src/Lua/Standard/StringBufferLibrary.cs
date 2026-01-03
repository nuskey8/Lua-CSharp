using System.Buffers;
using System.Runtime.CompilerServices;
using System.Text;

namespace Lua.Standard;

using LightUserData = (double value, object referenceValue);

public interface IStringBufferReader
{
    public LuaState State { set; }
    public Stream? Stream { set; }

    public bool ReadBoolean();
    public byte ReadByte();
    public double ReadDouble();
    public short ReadInt16();
    public int ReadInt32();
    public long ReadInt64();
    public sbyte ReadSByte();
    public float ReadSingle();
    public string ReadString();
    public ushort ReadUInt16();
    public uint ReadUInt32();
    public ulong ReadUInt64();

    public LightUserData ReadLightUserData();
    public ILuaUserData ReadUserData();
}

public interface IStringBufferWriter
{
    public LuaState State { set; }
    public Stream? Stream { set; }

    public void Write(bool boolean);
    public void Write(byte @byte);
    public void Write(double @double);
    public void Write(int int32);
    public void Write(long int64);
    public void Write(short int16);
    public void Write(sbyte @sbyte);
    public void Write(float single);
    public void Write(string @string);
    public void Write(uint uint32);
    public void Write(ulong uint64);
    public void Write(ushort uint16);

    public void Write(LightUserData lightUserData);
    public void Write(ILuaUserData userData);
}

public sealed class StringBufferReader : IStringBufferReader
{
    public BinaryReader binaryReader = null!;

    public LuaState State { get; set; } = null!;

    public Stream? Stream
    {
        set
        {
            binaryReader?.Dispose();
            if (value is not null)
            {
                binaryReader = new BinaryReader(value, Encoding.Default, true);
            }
            else
            {
                binaryReader = null!;
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool ReadBoolean() => binaryReader.ReadBoolean();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public byte ReadByte() => binaryReader.ReadByte();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public double ReadDouble() => binaryReader.ReadDouble();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public short ReadInt16() => binaryReader.ReadInt16();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int ReadInt32() => binaryReader.ReadInt32();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public long ReadInt64() => binaryReader.ReadInt64();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public sbyte ReadSByte() => binaryReader.ReadSByte();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public float ReadSingle() => binaryReader.ReadSingle();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public string ReadString() => binaryReader.ReadString();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ushort ReadUInt16() => binaryReader.ReadUInt16();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public uint ReadUInt32() => binaryReader.ReadUInt32();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ulong ReadUInt64() => binaryReader.ReadUInt64();

    public LightUserData ReadLightUserData()
    {
        throw new LuaRuntimeException(State, $"attempt to deserialize unsupported object type: {LuaValueType.LightUserData}");
    }

    public ILuaUserData ReadUserData()
    {
        throw new LuaRuntimeException(State, $"attempt to deserialize unsupported object type: {LuaValueType.UserData}");
    }
}

public sealed class StringBufferWriter : IStringBufferWriter
{
    public BinaryWriter binaryWriter = null!;

    public LuaState State { get; set; } = null!;

    public Stream? Stream
    {
        set
        {
            binaryWriter?.Dispose();
            if (value is not null)
            {
                binaryWriter = new BinaryWriter(value, Encoding.Default, true);
            }
            else
            {
                binaryWriter = null!;
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Write(bool boolean) => binaryWriter.Write(boolean);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Write(byte @byte) => binaryWriter.Write(@byte);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Write(double @double) => binaryWriter.Write(@double);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Write(int int32) => binaryWriter.Write(int32);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Write(long int64) => binaryWriter.Write(int64);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Write(short int16) => binaryWriter.Write(int16);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Write(sbyte @sbyte) => binaryWriter.Write(@sbyte);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Write(float single) => binaryWriter.Write(single);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Write(string @string) => binaryWriter.Write(@string);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Write(uint uint32) => binaryWriter.Write(uint32);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Write(ulong uint64) => binaryWriter.Write(uint64);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Write(ushort uint16) => binaryWriter.Write(uint16);

    public void Write(LightUserData lightUserData)
    {
        throw new LuaRuntimeException(State, $"attempt to serialize unsupported object type: {LuaValueType.LightUserData}");
    }

    public void Write(ILuaUserData userData)
    {
        throw new LuaRuntimeException(State, $"attempt to serialize unsupported object type: {LuaValueType.UserData}");
    }
}

public abstract class StringBufferLibrary<TReader, TWriter>
    where TReader : IStringBufferReader, new()
    where TWriter : IStringBufferWriter, new()
{
    private struct Options(int maxRecursions)
    {
        public int maxRecursions = maxRecursions;
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
        public required MemoryStream memoryStream;
        public required Options options;
        public readonly TReader reader = new();
        public readonly TWriter writer = new();

        public string BufferToString()
        {
            if (!memoryStream.TryGetBuffer(out ArraySegment<byte> buffer))
            {
                throw new InvalidOperationException("MemoryStream has no accessible buffer");
            }
            return Convert.ToBase64String(buffer.Array, buffer.Offset, buffer.Count);
        }
    }

    public const string LIBRARY_NAME = "string.buffer";

    private const byte TOKEN_NIL = (byte)LuaValueType.Nil;
    private const byte TOKEN_BOOLEAN = (byte)LuaValueType.Boolean;
    private const byte TOKEN_STRING = (byte)LuaValueType.String;
    private const byte TOKEN_NUMBER = (byte)LuaValueType.Number;
    private const byte TOKEN_FUNCTION = (byte)LuaValueType.Function;
    private const byte TOKEN_THREAD = (byte)LuaValueType.Thread;
    private const byte TOKEN_LIGHT_USER_DATA = (byte)LuaValueType.LightUserData;
    private const byte TOKEN_USER_DATA = (byte)LuaValueType.UserData;
    private const byte TOKEN_TABLE = (byte)LuaValueType.Table;

    private static readonly ConditionalWeakTable<LuaUserData, BufferObjectData> bufferObjectDataTable = [];

    internal readonly LibraryFunction[] Functions;

    private readonly LuaTable bufferObjectMetatable;

    private readonly BufferObjectData defaultBufferObjectData = new()
    {
        memoryStream = new MemoryStream(),
        options = new Options(32),
    };

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
                ["get"] = GenerateGetFunction(),
            }
        };
        bufferObjectMetatable["__length"] = GenerateMetatableLengthFunction();
        bufferObjectMetatable["__tostring"] = GenerateMetatableToStringFunction();
    }

    private async ValueTask<int> New(LuaFunctionExecutionContext context, CancellationToken cancellationToken)
    {
        int size = New_Arg_Size(context);
        Options options = New_Arg_Options(context);

        LuaUserData bufferObject = new(LuaValue.Nil, bufferObjectMetatable);

        bufferObjectDataTable.AddOrUpdate(bufferObject, new BufferObjectData()
        {
            memoryStream = new MemoryStream(size),
            options = options,
        });

        return context.Return(new LuaValue(bufferObject));
    }

    private int New_Arg_Size(LuaFunctionExecutionContext context)
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

    private Options New_Arg_Options(LuaFunctionExecutionContext context)
    {
        Options options = defaultBufferObjectData.options;
        if (context.ArgumentCount >= 2)
        {
            LuaValue arg2 = context.Arguments[0];
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

    private async ValueTask<int> Encode(LuaFunctionExecutionContext context, CancellationToken cancellationToken)
    {
        return EncodeImplement(context, defaultBufferObjectData);
    }

    private async ValueTask<int> Decode(LuaFunctionExecutionContext context, CancellationToken cancellationToken)
    {
        return DecodeImplement(context, defaultBufferObjectData);
    }

    private LuaFunction GenerateEncodeFunction()
    {
        ValueTask<int> StringBufferObjectEncode(LuaFunctionExecutionContext context, CancellationToken cancellationToken)
        {
            (LuaUserData self, BufferObjectData data) = StringBufferObjectFetchSelf(context);
            return new(EncodeImplement(context, data));
        }
        return new LuaFunction("string.buffer.object.encode", StringBufferObjectEncode);
    }

    private LuaFunction GenerateDecodeFunction()
    {
        ValueTask<int> StringBufferObjectEncode(LuaFunctionExecutionContext context, CancellationToken cancellationToken)
        {
            (LuaUserData self, BufferObjectData data) = StringBufferObjectFetchSelf(context);
            return new(DecodeImplement(context, data));
        }
        return new LuaFunction("string.buffer.object.decode", StringBufferObjectEncode);
    }

    private LuaFunction GenerateResetFunction()
    {
        static ValueTask<int> StringBufferObjectReset(LuaFunctionExecutionContext context, CancellationToken cancellationToken)
        {
            (LuaUserData self, BufferObjectData data) = StringBufferObjectFetchSelf(context);
            data.memoryStream.SetLength(0);
            data.memoryStream.Position = 0;
            return new(context.Return(new LuaValue(self)));
        }
        return new LuaFunction("string.buffer.object.reset", StringBufferObjectReset);
    }

    private LuaFunction GenerateFreeFunction()
    {
        static ValueTask<int> StringBufferObjectReset(LuaFunctionExecutionContext context, CancellationToken cancellationToken)
        {
            (LuaUserData self, BufferObjectData data) = StringBufferObjectFetchSelf(context);
            data.memoryStream = new MemoryStream();
            return new(context.Return(new LuaValue(self)));
        }
        return new LuaFunction("string.buffer.object.free", StringBufferObjectReset);
    }

    private LuaFunction GeneratePutFunction()
    {
        ValueTask<int> StringBufferObjectPut(LuaFunctionExecutionContext context, CancellationToken cancellationToken)
        {
            (LuaUserData self, BufferObjectData data) = StringBufferObjectFetchSelf(context);
            for (int i = 1; i < context.ArgumentCount; i++)
            {
                PutValue(context, data, i);
            }
            return new(context.Return(new LuaValue(self)));
        }
        return new LuaFunction("string.buffer.object.put", StringBufferObjectPut);
    }

    private LuaFunction GeneratePutFFunction()
    {
        LuaValue formatFunction = new LuaFunction(StringLibrary.Instance.Format);
        LuaValue[] arguments = [];

        ValueTask<int> StringBufferObjectPutF(LuaFunctionExecutionContext context, CancellationToken cancellationToken)
        {
            (LuaUserData self, BufferObjectData data) = StringBufferObjectFetchSelf(context);

            if (context.ArgumentCount > arguments.Length)
            {
                arguments = new LuaValue[context.ArgumentCount];
            }
            for (int i = 0; i < context.ArgumentCount; i++)
            {
                arguments[i] = context.Arguments[i];
            }

            LuaValue[] results = context.State.CallAsync(formatFunction, arguments.AsSpan()).Result;

            for (int i = 0; i < context.ArgumentCount; i++)
            {
                arguments[i] = LuaValue.Nil;
            }

            return new(context.Return(new LuaValue(self)));
        }
        return new LuaFunction("string.buffer.object.putf", StringBufferObjectPutF);
    }

    private LuaFunction GenerateGetFunction()
    {
        ValueTask<int> StringBufferObjectPutF(LuaFunctionExecutionContext context, CancellationToken cancellationToken)
        {
            (LuaUserData self, BufferObjectData data) = StringBufferObjectFetchSelf(context);

            //

            return new(context.Return(new LuaValue(self)));
        }
        return new LuaFunction("string.buffer.object.putf", StringBufferObjectPutF);
    }

    private LuaFunction GenerateMetatableLengthFunction()
    {
        static ValueTask<int> MetatableLength(LuaFunctionExecutionContext context, CancellationToken cancellationToken)
        {
            (LuaUserData self, BufferObjectData data) = StringBufferObjectFetchSelf(context);
            return new(context.Return(Encoding.Default.GetString(data.memoryStream.GetBuffer(), 0, (int)data.memoryStream.Length)));
        }
        return new LuaFunction("string.buffer.object.<metatable>.__length", MetatableLength);
    }

    private LuaFunction GenerateMetatableToStringFunction()
    {
        static ValueTask<int> MetatableToString(LuaFunctionExecutionContext context, CancellationToken cancellationToken)
        {
            (LuaUserData self, BufferObjectData data) = StringBufferObjectFetchSelf(context);
            return new(context.Return(data.BufferToString()));
        }
        return new LuaFunction("string.buffer.object.<metatable>.__tostring", MetatableToString);
    }

    private static (LuaUserData self, BufferObjectData data) StringBufferObjectFetchSelf(LuaFunctionExecutionContext context)
    {
        if (context.ArgumentCount == 0)
        {
            throw new LuaRuntimeException(context.State, "bad argument #1 to 'self': userdata expected, got nil");
        }
        if (!context.Arguments[0].TryRead(out LuaUserData bufferObject))
        {
            throw new LuaRuntimeException(context.State, $"bad argument #1 to 'self': userdata expected, got {context.Arguments[0].TypeToString()}");
        }
        if (!bufferObjectDataTable.TryGetValue(bufferObject, out BufferObjectData? data))
        {
            throw new LuaRuntimeException(context.State, $"bad argument #1 to 'self': userdata is not a string buffer object");
        }
        return (bufferObject, data);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int EncodeImplement(LuaFunctionExecutionContext context, BufferObjectData data)
    {
        LuaValue value = context.ArgumentCount < 1 ? LuaValue.Nil : context.Arguments[0];
        SerialContext serialContext = new(context, defaultBufferObjectData);

        data.memoryStream.Position = 0;
        data.writer.State = context.State;
        data.writer.Stream = data.memoryStream;

        WriteLuaValue(value, serialContext);

        string result = data.BufferToString();
        data.writer.Stream = null;
        return context.Return(result);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int DecodeImplement(LuaFunctionExecutionContext context, BufferObjectData bufferObjectData)
    {
        LuaValue value = context.ArgumentCount < 1 ? LuaValue.Nil : context.Arguments[0];
        if (!value.TryReadString(out string data))
        {
            throw new LuaRuntimeException(context.State, $"bad argument to #1 'data': string expected, got {value.TypeToString()}");
        }

        SerialContext serialContext = new(context, bufferObjectData);

        byte[] bytes = ArrayPool<byte>.Shared.Rent(data.Length * 3 / 4);
        try
        {
            if (!Convert.TryFromBase64String(data, bytes, out int bytesWritten))
            {
                throw new FormatException("Invalid base64 string");
            }
            bufferObjectData.memoryStream.SetLength(0);
            bufferObjectData.memoryStream.Write(bytes, 0, bytesWritten);
            bufferObjectData.memoryStream.Position = 0;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(bytes);
        }
        bufferObjectData.reader.State = context.State;
        bufferObjectData.reader.Stream = bufferObjectData.memoryStream;

        LuaValue result = ReadLuaValue(serialContext);

        bufferObjectData.reader.Stream = null;
        return context.Return(result);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void WriteLuaValue(LuaValue value, SerialContext serialContext)
    {
        serialContext.data.writer.Write((byte)value.Type);
        switch (value.Type)
        {
            case LuaValueType.Nil:
                break;
            case LuaValueType.Boolean:
                serialContext.data.writer.Write(value.UnsafeRead<bool>());
                break;
            case LuaValueType.String:
                serialContext.data.writer.Write(value.UnsafeReadString());
                break;
            case LuaValueType.Number:
                serialContext.data.writer.Write(value.UnsafeReadDouble());
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
                WriteTable(value.Read<LuaTable>(), serialContext.NextDepth());
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
        TWriter writer = serialContext.data.writer;
        writer.Write(lightUserData);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void WriteUserData(SerialContext serialContext, ILuaUserData userData)
    {
        TWriter writer = serialContext.data.writer;
        writer.Write(userData);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void WriteTable(LuaTable luaTable, SerialContext serialContext)
    {
        if (serialContext.depth >= serialContext.data.options.maxRecursions)
        {
            throw DeeplyNestedTableException(serialContext.lua.State);
        }

        TWriter writer = serialContext.data.writer;
        writer.Write(luaTable.ArrayLength);
        writer.Write(luaTable.HashMapCount);
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

        TReader reader = serialContext.data.reader;
        byte token = reader.ReadByte();
        return token switch
        {
            TOKEN_NIL => LuaValue.Nil,
            TOKEN_BOOLEAN => (LuaValue)reader.ReadBoolean(),
            TOKEN_STRING => (LuaValue)reader.ReadString(),
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
    private LuaTable ReadTable(SerialContext serialContext)
    {
        TReader reader = serialContext.data.reader;
        int arrayLength = reader.ReadInt32();
        int hashMapCount = reader.ReadInt32();
        LuaTable table = new(arrayLength, hashMapCount);
        for (int _ = 0; _ < arrayLength + hashMapCount; _++)
        {
            LuaValue key = ReadLuaValue(serialContext);
            LuaValue value = ReadLuaValue(serialContext);
            table[key] = value;
        }
        return table;
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
        TReader reader = serialContext.data.reader;
        (double value, object? referenceValue) = reader.ReadLightUserData();
        return new LuaValue(LuaValueType.LightUserData, value, referenceValue);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private LuaValue ReadUserData(SerialContext serialContext)
    {
        TReader reader = serialContext.data.reader;
        return new LuaValue(reader.ReadUserData());
    }

    readonly LuaValue[] _PutValue_arguments = [LuaValue.Nil];

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void PutValue(LuaFunctionExecutionContext context, BufferObjectData data, int index)
    {
        LuaValue value = context.Arguments[index];
        switch (value.Type)
        {
            case LuaValueType.String:
                data.writer.Write(value.UnsafeReadString());
                break;
            case LuaValueType.Number:
                data.writer.Write(value.UnsafeReadDouble());
                break;
            case LuaValueType.Table:
                {
                    var luaTable = value.Read<LuaTable>();
                    if (luaTable.Metatable is null || !luaTable.Metatable.TryGetValue("__tostring", out LuaValue tostring))
                    {
                        throw new LuaRuntimeException(context.State, $"bad argument #2 to 'put': table require meta-method '__tostring'");
                    }
                    _PutValue_arguments[0] = luaTable;
                    LuaValue[] results = context.State.CallAsync(tostring, _PutValue_arguments).Result;
                    if (results.Length >= 1)
                    {
                        data.writer.Write(results[0].ToString());
                    }
                }
                break;
            default:
                throw new LuaRuntimeException(context.State, $"bad argument #2 to 'put': string,number/table expected, got {value.TypeToString()}");
        }
    }

    private Exception DeeplyNestedTableException(LuaState luaState)
    {
        return new LuaRuntimeException(luaState, "failed to serialize deeply nested table");
    }
}

public sealed class StringBufferLibrary : StringBufferLibrary<StringBufferReader, StringBufferWriter>
{
    public static readonly StringBufferLibrary Instance = new();
}
