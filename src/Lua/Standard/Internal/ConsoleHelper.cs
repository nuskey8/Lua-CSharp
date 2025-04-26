namespace Lua.Standard.Internal;

public class ConsoleHelper
{
    public static bool SupportStandardConsole => LuaPlatformUtility.IsSandBox;

    private static Stream? _inputStream;
    private static TextReader? _inputReader;

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