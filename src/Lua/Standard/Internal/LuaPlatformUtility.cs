namespace Lua.Standard.Internal;

static class LuaPlatformUtility
{
    public static bool IsSandBox => SupportStdio;
    public static bool SupportStdio => supportStdioTryLazy.Value;

    static Lazy<bool> supportStdioTryLazy = new(() =>
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