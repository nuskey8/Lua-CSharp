using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
namespace Lua.Unity
{
    public sealed class ResourcesModuleLoader : ILuaModuleLoader
    {
        readonly Dictionary<string, LuaAssetBase> cache = new();

        public bool Exists(string moduleName)
        {
            Debug.Log(moduleName);
            if (cache.TryGetValue(moduleName, out _)) return true;

            var asset = Resources.Load<LuaAssetBase>(moduleName);
            if (asset == null) return false;

            cache.Add(moduleName, asset);
            return true;
        }

        public async ValueTask<LuaModule> LoadAsync(string moduleName, CancellationToken cancellationToken = default)
        {
            if (cache.TryGetValue(moduleName, out var asset))
            {
                return asset.GetModule(moduleName);
            }

            var request = Resources.LoadAsync<LuaAssetBase>(moduleName);
            await request;

            if (request.asset == null)
            {
                throw new LuaModuleNotFoundException(moduleName);
            }

            asset = (LuaAssetBase)request.asset;
            cache.Add(moduleName, asset);
            return asset.GetModule(moduleName);        }
    }

#if !UNITY_2023_1_OR_NEWER
    internal static class ResourceRequestExtensions
    {
        public static ResourceRequestAwaiter GetAwaiter(this ResourceRequest request)
        {
            return new ResourceRequestAwaiter(request);
        }

        public readonly struct ResourceRequestAwaiter : ICriticalNotifyCompletion
        {
            public ResourceRequestAwaiter(ResourceRequest request)
            {
                this.request = request;
            }

            readonly ResourceRequest request;

            public bool IsCompleted => request.isDone;

            public void OnCompleted(Action continuation)
            {
                request.completed += x => continuation.Invoke();
            }

            public void UnsafeOnCompleted(Action continuation)
            {
                request.completed += x => continuation.Invoke();
            }
            
            public void GetResult()
            {
            }

            public ResourceRequestAwaiter GetAwaiter()
            {
                return this;
            }
        }
    }
#endif
}