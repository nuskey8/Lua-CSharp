using Lua.Standard.Internal;

namespace Lua.IO;

/// <summary>
/// Default implementation of ILuaStandardIO using Console
/// </summary>
public sealed class ConsoleStandardIO : ILuaStandardIO
{
    ILuaIOStream? standardInput;

    public ILuaIOStream Input => standardInput ??=
        new StandardIOStream(ILuaIOStream.CreateStreamWrapper(
            ConsoleHelper.OpenStandardInput(),
            LuaFileOpenMode.Read));

    ILuaIOStream? standardOutput;

    public ILuaIOStream Output

        => standardOutput ??=
            new StandardIOStream(ILuaIOStream.CreateStreamWrapper(
                ConsoleHelper.OpenStandardOutput(),
                LuaFileOpenMode.Write));


    ILuaIOStream? standardError;

    public ILuaIOStream Error => standardError ??=
        new StandardIOStream(ILuaIOStream.CreateStreamWrapper(
            ConsoleHelper.OpenStandardError(),
            LuaFileOpenMode.Write));
}