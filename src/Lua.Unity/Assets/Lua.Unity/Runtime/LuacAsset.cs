using UnityEngine;

namespace Lua.Unity
{
    public sealed class LuacAsset : LuaAssetBase
    {
        [HideInInspector]
        [SerializeField] internal byte[] bytes;
        public byte[] Bytes => bytes;
        public override LuaModule GetModule(string searchedName)
        {
             return new LuaModule(searchedName,bytes);
        }
    }
}