using UnityEngine;

namespace Lua.Unity
{

    public abstract class LuaAssetBase : ScriptableObject
    {
        public abstract LuaModule GetModule(string searchedName);
    }
}