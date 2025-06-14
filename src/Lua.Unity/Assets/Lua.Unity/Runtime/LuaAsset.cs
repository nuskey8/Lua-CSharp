using UnityEngine;

namespace Lua.Unity
{
    public sealed class LuaAsset : LuaAssetBase
    {
        [SerializeField] internal string text;
        public string Text => text;
        
        public override LuaModule GetModule(string searchedName)
        {
            return new LuaModule(searchedName,text);
        }
    }
}