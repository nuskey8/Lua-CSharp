using Lua.Standard.Internal;

namespace Lua.IO;

/// <summary>
/// Default implementation of ILuaStandardIO using Console
/// </summary>
public sealed class ConsoleStandardIO : ILuaStandardIO
{
    ILuaStream? standardInput;

    public ILuaStream Input => standardInput ??=
        new StandardIOStream(ILuaStream.CreateStreamWrapper(
            ConsoleHelper.OpenStandardInput(),
            LuaFileOpenMode.Read));

    ILuaStream? standardOutput;

    public ILuaStream Output

        => standardOutput ??=
            new StandardIOStream(ILuaStream.CreateStreamWrapper(
                ConsoleHelper.OpenStandardOutput(),
                LuaFileOpenMode.Write));


    ILuaStream? standardError;

    public ILuaStream Error => standardError ??=
        new StandardIOStream(ILuaStream.CreateStreamWrapper(
            ConsoleHelper.OpenStandardError(),
            LuaFileOpenMode.Write));
}