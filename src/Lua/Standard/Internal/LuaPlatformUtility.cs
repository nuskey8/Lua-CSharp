namespace Lua.Standard.Internal;

public class LuaPlatformUtility
{
    public static bool IsSandBox => SupportStdio;
    public static bool SupportStdio => _supportStdioTryLazy.Value;
    
    private static Lazy<bool> _supportStdioTryLazy = new Lazy<bool>(() =>
    {
        try
        {
#if NET6_0_OR_GREATER
            var isDesktop = System.OperatingSystem.IsWindows() || 
                            System.OperatingSystem.IsLinux() || 
                            System.OperatingSystem.IsMacOS();
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