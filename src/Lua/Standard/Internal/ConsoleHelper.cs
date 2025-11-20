namespace Lua.Standard.Internal;

static class ConsoleHelper
{
    public static bool SupportStandardConsole => LuaPlatformUtility.IsSandBox;

    static Stream? _inputStream;
    static TextReader? _inputReader;

    public static Stream OpenStandardInput()
    {
        if (SupportStandardConsole)
        {
            return Console.OpenStandardInput();
        }

        _inputStream ??= new MemoryStream();
        _inputReader ??= new StreamReader(_inputStream);
        return _inputStream;
    }

    public static int Read()
    {
        if (SupportStandardConsole)
        {
            return Console.Read();
        }

        return _inputReader?.Read() ?? 0;
    }

    public static Stream OpenStandardOutput()
    {
        return Console.OpenStandardOutput();
    }

    public static Stream OpenStandardError()
    {
        return Console.OpenStandardError();
    }
}