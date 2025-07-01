using Lua.Runtime;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Lua.Unity
{
    public static class LuaThreadAssetExtensions
    {
        public static ValueTask<LuaValue[]> ExecuteAsync(this LuaThreadAccess access, LuaAssetBase luaAssetBase, string name, CancellationToken cancellationToken = default)
        {
            if (luaAssetBase == null)
            {
                throw new ArgumentNullException(nameof(luaAssetBase));
            }

            var module = luaAssetBase.GetModule(name);
            var closure = module.Type == LuaModuleType.Bytes
                ? access.State.Load(module.ReadBytes(), module.Name)
                : access.State.Load(module.ReadText(), module.Name);
            return access.ExecuteAsync(closure, cancellationToken);
        }

        public static ValueTask<int> ExecuteAsync(this LuaThreadAccess access, LuaAssetBase luaAssetBase, string name, Memory<LuaValue> buffer, CancellationToken cancellationToken = default)
        {
            if (luaAssetBase == null)
            {
                throw new ArgumentNullException(nameof(luaAssetBase));
            }

            var module = luaAssetBase.GetModule(name);
            var closure = module.Type == LuaModuleType.Bytes
                ? access.State.Load(module.ReadBytes(), module.Name)
                : access.State.Load(module.ReadText(), module.Name);
            return access.ExecuteAsync(closure, buffer, cancellationToken);
        }

        public static ValueTask<LuaValue[]> ExecuteAsync(this LuaState state, LuaAssetBase luaAssetBase, string name, CancellationToken cancellationToken = default)
        {
            return state.RootAccess.ExecuteAsync(luaAssetBase, name, cancellationToken);
        }

        public static ValueTask<int> ExecuteAsync(this LuaState state, LuaAssetBase luaAssetBase, string name, Memory<LuaValue> buffer, CancellationToken cancellationToken = default)
        {
            return state.RootAccess.ExecuteAsync(luaAssetBase, name, buffer, cancellationToken);
        }
    }
}