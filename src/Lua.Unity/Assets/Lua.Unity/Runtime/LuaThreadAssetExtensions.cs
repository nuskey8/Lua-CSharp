using System;
using System.Threading;
using System.Threading.Tasks;
using Lua.Runtime;

namespace Lua.Unity
{
    public static class LuaThreadAssetExtensions
    {
        public static ValueTask<int> ExecuteAsync(
            this LuaState state,
            LuaAssetBase luaAssetBase,
            string name,
            Memory<LuaValue> buffer,
            CancellationToken cancellationToken = default
        )
        {
            if (luaAssetBase == null)
            {
                throw new ArgumentNullException(nameof(luaAssetBase));
            }

            var module = luaAssetBase.GetModule(name);
            var closure = state.LoadModule(module);
            return state.ExecuteAsync(closure, buffer, cancellationToken);
        }

        public static ValueTask<LuaValue[]> ExecuteAsync(
            this LuaState state,
            LuaAssetBase luaAssetBase,
            string name,
            CancellationToken cancellationToken = default
        )
        {
            if (luaAssetBase == null)
            {
                throw new ArgumentNullException(nameof(luaAssetBase));
            }

            var module = luaAssetBase.GetModule(name);
            return state.ExecuteAsync(state.LoadModule(module), cancellationToken);
        }
    }
}
