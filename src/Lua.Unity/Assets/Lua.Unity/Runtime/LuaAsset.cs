using UnityEngine;

namespace Lua.Unity
{
    public sealed class LuaAsset : LuaAssetBase
    {
        [SerializeField] internal string text;
        public string Text => text;
        public override LuaFileContent Content  => new LuaFileContent(text);
    }
    
   
}