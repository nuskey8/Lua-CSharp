#if LUA_UNITY_ADDRESSABLES
using Lua.IO;
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
    public sealed class AddressablesLuaFileLoader : ILuaFileLoader
    {
        readonly Dictionary<string, LuaAssetBase> cache = new();

        public bool Exists(string path)
        {
            if(!LuaFileLoaderUtility.TryGetLuaAssetType(path,out _))return false;
            if (cache.ContainsKey(path)) return true;

            var location = Addressables.LoadResourceLocationsAsync(path,typeof(LuaAssetBase)).WaitForCompletion();
            return location.Any();
        }

        public async ValueTask<ILuaStream> LoadAsync(string path, CancellationToken cancellationToken = default)
        {
            if (cache.TryGetValue(path, out var asset))
            {
                return ILuaStream.CreateFromFileContent(asset.Content);
            }

            var asyncOperation = Addressables.LoadAssetAsync<LuaAssetBase>(path);
            asset = await asyncOperation;

            if (asset == null)
            {
                throw new LuaModuleNotFoundException(path);
            }

            cache.Add(path, asset);
            return ILuaStream.CreateFromFileContent(asset.Content);
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