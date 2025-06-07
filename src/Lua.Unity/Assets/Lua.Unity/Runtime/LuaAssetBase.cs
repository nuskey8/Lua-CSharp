using UnityEngine;

namespace Lua.Unity
{
   [Icon( "Packages/Lua.Unity/Editor/Resources/Icons/LuaIcon.png" )]

    public abstract class LuaAssetBase : ScriptableObject
    {
        public abstract LuaFileContent Content { get; }
    }
}