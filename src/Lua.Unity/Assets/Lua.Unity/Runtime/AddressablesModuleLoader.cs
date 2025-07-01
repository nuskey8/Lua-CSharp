#if LUA_UNITY_ADDRESSABLES
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace Lua.Unity
{
    public sealed class AddressablesModuleLoader : ILuaModuleLoader
    {
        readonly Dictionary<string, LuaAssetBase> cache = new();

        public bool Exists(string moduleName)
        {
            if (cache.TryGetValue(moduleName, out _)) return true;

            var location = Addressables.LoadResourceLocationsAsync(moduleName).WaitForCompletion();
            return location.Any();
        }

        public async ValueTask<LuaModule> LoadAsync(string moduleName, CancellationToken cancellationToken = default)
        {
            if (cache.TryGetValue(moduleName, out var asset))
            {
                
                return asset .GetModule( moduleName);
            }

            var asyncOperation = Addressables.LoadAssetAsync<LuaAsset>(moduleName);
            asset = await asyncOperation;

            if (asset == null)
            {
                throw new LuaModuleNotFoundException(moduleName);
            }

            cache.Add(moduleName, asset);
            return asset .GetModule( moduleName);
        }
    }
    internal static class AsyncOperationHandleExtensions
    {
        public static AsyncOperationHandleAwaiter<T> GetAwaiter<T>(this AsyncOperationHandle<T> asyncOperationHandle)
        {
            return new AsyncOperationHandleAwaiter<T>(asyncOperationHandle);
        }

        public readonly struct AsyncOperationHandleAwaiter<T> : ICriticalNotifyCompletion
        {
            public AsyncOperationHandleAwaiter(AsyncOperationHandle<T> asyncOperationHandle)
            {
                this.asyncOperationHandle = asyncOperationHandle;
            }

            readonly AsyncOperationHandle<T> asyncOperationHandle;

            public bool IsCompleted => asyncOperationHandle.IsDone;

            public void OnCompleted(Action continuation)
            {
                asyncOperationHandle.Completed += x => continuation.Invoke();
            }

            public void UnsafeOnCompleted(Action continuation)
            {
                asyncOperationHandle.Completed += x => continuation.Invoke();
            }

            public T GetResult()
            {
                return asyncOperationHandle.Result;
            }

            public AsyncOperationHandleAwaiter<T> GetAwaiter()
            {
                return this;
            }
        }
    }
}
#endif