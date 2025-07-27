using Lua.Standard.Internal;

namespace Lua.IO;

/// <summary>
/// Default implementation of ILuaStandardIO using Console
/// </summary>
public sealed class ConsoleStandardIO : ILuaStandardIO
{
    ILuaStream? standardInput;

    public ILuaStream Input =>
        standardInput ??=
            new StandardIOStream(ILuaStream.CreateFromStream(
                ConsoleHelper.OpenStandardInput(),
                LuaFileOpenMode.Read));

    ILuaStream? standardOutput;

    public ILuaStream Output =>
        standardOutput ??=
            new StandardIOStream(ILuaStream.CreateFromStream(
                ConsoleHelper.OpenStandardOutput(),
                LuaFileOpenMode.Write));


    ILuaStream? standardError;

    public ILuaStream Error =>
        standardError ??=
            new StandardIOStream(ILuaStream.CreateFromStream(
                ConsoleHelper.OpenStandardError(),
                LuaFileOpenMode.Write));
}