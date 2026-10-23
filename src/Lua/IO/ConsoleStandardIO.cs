using Lua.Standard.Internal;

namespace Lua.IO;

/// <summary>
/// Default implementation of ILuaStandardIO using Console
/// </summary>
/// <param name="stringMode">
/// Selects UTF-8 text I/O for <see cref="LuaStringMode.Utf16"/>, or raw byte I/O for <see cref="LuaStringMode.Bytes"/>.
/// Use the same mode as <see cref="Lua.Platforms.LuaPlatform.StringMode"/>.
/// </param>
public sealed class ConsoleStandardIO(LuaStringMode stringMode) : ILuaStandardIO
{
    public ConsoleStandardIO()
        : this(LuaStringMode.Utf16) { }

    ILuaStream? standardInput;
    ILuaStream? standardOutput;
    ILuaStream? standardError;

    public ILuaStream Input =>
        standardInput ??= new StandardIOStream(
            ILuaStream.CreateFromStream(
                ConsoleHelper.OpenStandardInput(),
                LuaFileOpenMode.Read,
                stringMode
            )
        );

    public ILuaStream Output =>
        standardOutput ??= new StandardIOStream(
            ILuaStream.CreateFromStream(
                ConsoleHelper.OpenStandardOutput(),
                LuaFileOpenMode.Write,
                stringMode
            )
        );

    public ILuaStream Error =>
        standardError ??= new StandardIOStream(
            ILuaStream.CreateFromStream(
                ConsoleHelper.OpenStandardError(),
                LuaFileOpenMode.Write,
                stringMode
            )
        );
}
