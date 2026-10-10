using System;
using UnityEditor;

namespace Lua.Unity.Editor
{
    [InitializeOnLoad]
    internal sealed class LuaSourceGeneratorImporter : AssetPostprocessor
    {
        private const string GeneratorPath =
            "Packages/com.nuskey8.lua.unity.internal/Lua.SourceGenerator.dll";

        static LuaSourceGeneratorImporter()
        {
            EditorApplication.delayCall += Configure;
        }

        private static void OnPostprocessAllAssets(
            string[] importedAssets,
            string[] deletedAssets,
            string[] movedAssets,
            string[] movedFromAssetPaths
        )
        {
            if (Array.IndexOf(importedAssets, GeneratorPath) >= 0)
            {
                EditorApplication.delayCall += Configure;
            }
        }

        private static void Configure()
        {
            if (AssetImporter.GetAtPath(GeneratorPath) is not PluginImporter importer)
            {
                return;
            }

            var changed = false;
            if (importer.GetCompatibleWithAnyPlatform())
            {
                importer.SetCompatibleWithAnyPlatform(false);
                changed = true;
            }

            if (importer.GetCompatibleWithEditor())
            {
                importer.SetCompatibleWithEditor(false);
                changed = true;
            }

            var labels = AssetDatabase.GetLabels(importer);
            if (Array.IndexOf(labels, "RoslynAnalyzer") < 0)
            {
                var updatedLabels = new string[labels.Length + 1];
                Array.Copy(labels, updatedLabels, labels.Length);
                updatedLabels[labels.Length] = "RoslynAnalyzer";
                AssetDatabase.SetLabels(importer, updatedLabels);
                changed = true;
            }

            if (changed)
            {
                importer.SaveAndReimport();
            }
        }
    }
}
