using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Lua.Unity
{
    public sealed class LuaAsset : LuaAssetBase
    {
        [SerializeField] internal string text;
        public string Text => text;

        public override LuaModule GetModule(string searchedName)
        {
#if UNITY_EDITOR
            var moduleName = "@"+AssetDatabase.GetAssetPath(this);
#else
           var  moduleName =  $"@{searchedName}.lua";
#endif
            return new LuaModule(moduleName, text);
        }
    }
}