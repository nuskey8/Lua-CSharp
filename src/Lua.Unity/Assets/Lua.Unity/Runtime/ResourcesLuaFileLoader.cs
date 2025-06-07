using Lua.IO;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace Lua.Unity
{
    public sealed class ResourcesLuaFileLoader : ILuaFileLoader
    {
        readonly Dictionary<string, LuaAssetBase> cache = new();

        public bool Exists(string path)
        {
            if (!LuaFileLoaderUtility.TryGetLuaAssetType(path, out var type)) return false;
            if (cache.ContainsKey(path)) return true;
            var asset = Resources.Load(path[..^(type == LuaFileContentType.Binary ? 5 : 4)]); // Remove the ".lua" or .luac extension for loading
            if (asset == null || asset is not LuaAssetBase luaAsset) return false;
            cache.Add(path, luaAsset);
            switch (type)
            {
                case LuaFileContentType.Binary when luaAsset is LuacAsset:
                case LuaFileContentType.Text when luaAsset is LuaAsset:
                    return true;
                default:
                    return false;
            }
        }

        public ValueTask<ILuaStream> LoadAsync(string path, CancellationToken cancellationToken = default)
        {
            if (cache.TryGetValue(path, out var asset))
            {
                return new(ILuaStream.CreateFromFileContent(asset.Content));
            }

            throw new LuaModuleNotFoundException(path);
        }
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