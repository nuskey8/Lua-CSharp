using System.IO;
using UnityEditor.AssetImporters;
using UnityEngine;

namespace Lua.Unity.Editor
{
    [ScriptedImporter(1, "lua")]
    public sealed class LuaImporter : ScriptedImporter
    {
        public override void OnImportAsset(AssetImportContext ctx)
        {
            var text = File.ReadAllText(ctx.assetPath);
            var asset = ScriptableObject.CreateInstance<LuaAsset>();
            asset.text = text;
            ctx.AddObjectToAsset("Main", asset);
            ctx.SetMainObject(asset);
        }
    }
}
