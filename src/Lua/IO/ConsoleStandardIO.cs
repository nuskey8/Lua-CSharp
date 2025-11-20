using Lua.Standard.Internal;

namespace Lua.IO;

/// <summary>
/// Default implementation of ILuaStandardIO using Console
/// </summary>
public sealed class ConsoleStandardIO : ILuaStandardIO
{
    ILuaStream? standardInput;
    ILuaStream? standardOutput;
    ILuaStream? standardError;

    public ILuaStream Input =>
        standardInput ??=
            new StandardIOStream(ILuaStream.CreateFromStream(
                ConsoleHelper.OpenStandardInput(),
                LuaFileOpenMode.Read));

    public ILuaStream Output =>
        standardOutput ??=
            new StandardIOStream(ILuaStream.CreateFromStream(
                ConsoleHelper.OpenStandardOutput(),
                LuaFileOpenMode.Write));

    public ILuaStream Error =>
        standardError ??=
            new StandardIOStream(ILuaStream.CreateFromStream(
                ConsoleHelper.OpenStandardError(),
                LuaFileOpenMode.Write));
}