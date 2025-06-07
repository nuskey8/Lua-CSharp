using UnityEngine;

namespace Lua.Unity
{
    public sealed class LuacAsset : LuaAssetBase
    {
        [HideInInspector]
        [SerializeField] internal byte[] bytes;

        public byte[] Bytes => bytes;
        
        public override LuaFileContent Content  => new LuaFileContent(bytes);

    }
}