namespace Lua.Unity
{
    internal class LuaFileLoaderUtility
    {
        public static bool TryGetLuaAssetType(string path, out LuaFileContentType contentType)
        {
            if (path.EndsWith(".lua"))
            {
                contentType = LuaFileContentType.Text;
                return true;
            }
            if (path.EndsWith(".luac"))
            {
                contentType = LuaFileContentType.Binary;
                return true;
            }

            contentType = default;
            return false;
        }
    }
}