using Lua.Runtime;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Lua.Unity
{
    public static class LuaThreadAssetExtensions
    {

        public static ValueTask<int> ExecuteAsync(this LuaState state, LuaAssetBase luaAssetBase, string name, Memory<LuaValue> buffer, CancellationToken cancellationToken = default)
        {
            if (luaAssetBase == null)
            {
                throw new ArgumentNullException(nameof(luaAssetBase));
            }

            var module = luaAssetBase.GetModule(name);
            var closure = module.Type == LuaModuleType.Bytes
                ? state.Load(module.ReadBytes(), module.Name)
                : state.Load(module.ReadText(), module.Name);
            return state.ExecuteAsync(closure, buffer, cancellationToken);
        }

        public static ValueTask<LuaValue[]> ExecuteAsync(this LuaState state, LuaAssetBase luaAssetBase, string name, CancellationToken cancellationToken = default)
        {
            return state.ExecuteAsync(luaAssetBase, name, cancellationToken);
        }
    }
}