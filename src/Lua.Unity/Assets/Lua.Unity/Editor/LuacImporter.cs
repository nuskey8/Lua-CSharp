using System.IO;
using System.Runtime.CompilerServices;
using UnityEditor;
using UnityEditor.AssetImporters;
using UnityEngine;

namespace Lua.Unity.Editor
{
    [ScriptedImporter(1, "luac")]
    public sealed class LuacImporter : ScriptedImporter
    {
        static Texture2D icon;
        public override void OnImportAsset(AssetImportContext ctx)
        {
            var bytes = File.ReadAllBytes(ctx.assetPath);
            var asset = ScriptableObject.CreateInstance<LuacAsset>();
            if (icon == null)
            {
                icon = Resources.Load<Texture2D>("LuaIcon");
                if (icon == null)
                {
                    Debug.LogWarning("LuaAssetIcon not found in Resources. Using default icon.");
                    icon = EditorGUIUtility.IconContent("ScriptableObject Icon").image as Texture2D;
                }
            }
            EditorGUIUtility.SetIconForObject (asset,icon );
            asset.bytes = bytes;
            ctx.AddObjectToAsset("Main", asset);
            ctx.SetMainObject(asset);
        }
    }
}