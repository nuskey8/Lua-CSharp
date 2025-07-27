namespace Lua.Standard.Internal;

class LuaPlatformUtility
{
    public static bool IsSandBox => SupportStdio;

    public static bool SupportStdio => _supportStdioTryLazy.Value;

    static Lazy<bool> _supportStdioTryLazy = new(() =>
    {
        try
        {
#if NET6_0_OR_GREATER
            var isDesktop = OperatingSystem.IsWindows() ||
                            OperatingSystem.IsLinux() ||
                            OperatingSystem.IsMacOS();
            if (!isDesktop)
            {
                return false;
            }
#endif
            _ = Console.OpenStandardInput();
            _ = Console.OpenStandardOutput();
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    });
}