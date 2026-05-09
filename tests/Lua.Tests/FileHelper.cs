using System.Runtime.CompilerServices;

public static class FileHelper
{
    public static string GetAbsolutePath(
        string relativePath,
        [CallerFilePath] string callerFilePath = ""
    )
    {
        return Path.Combine(Path.GetDirectoryName(callerFilePath)!, relativePath);
    }
}
