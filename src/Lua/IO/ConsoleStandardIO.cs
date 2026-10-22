using Lua.Standard.Internal;

namespace Lua.IO;

/// <summary>
/// Default implementation of ILuaStandardIO using Console
/// </summary>
/// <param name="byteStrings">
/// When true, standard input and output are read and written as raw bytes (one byte per char)
/// instead of UTF-8 text. Use together with <see cref="Lua.Platforms.LuaPlatform.ByteStrings"/>.
/// </param>
public sealed class ConsoleStandardIO(bool byteStrings = false) : ILuaStandardIO
{
    ILuaStream? standardInput;
    ILuaStream? standardOutput;
    ILuaStream? standardError;

    public ILuaStream Input =>
        standardInput ??= new StandardIOStream(
            ILuaStream.CreateFromStream(
                ConsoleHelper.OpenStandardInput(),
                LuaFileOpenMode.Read,
                byteStrings
            )
        );

    public ILuaStream Output =>
        standardOutput ??= new StandardIOStream(
            ILuaStream.CreateFromStream(
                ConsoleHelper.OpenStandardOutput(),
                LuaFileOpenMode.Write,
                byteStrings
            )
        );

    public ILuaStream Error =>
        standardError ??= new StandardIOStream(
            ILuaStream.CreateFromStream(
                ConsoleHelper.OpenStandardError(),
                LuaFileOpenMode.Write,
                byteStrings
            )
        );
}
