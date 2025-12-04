using Lua.CodeAnalysis.Compilation;
using Lua.Runtime;
using System;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;

namespace Lua.Unity.Editor
{
    [CustomEditor(typeof(LuacAsset))]
    public sealed class LuacAssetEditor : UnityEditor.Editor
    {
        LuacAsset asset;
        static StringBuilder sb = new StringBuilder();
        private byte[] bytes;
        string prototypeCacheString;

        public override void OnInspectorGUI()
        {
            if (asset == null) asset = (LuacAsset)serializedObject.targetObject;
            if (bytes == null || !asset.bytes.AsSpan().SequenceEqual(bytes))
            {
                var prototype = Prototype.FromBytecode(asset.bytes.AsSpan(), asset.name);
                if (sb == null)
                    sb = new StringBuilder();
                sb.Clear();
                DebugChunk(sb, prototype, 0, 0);
                prototypeCacheString = sb.ToString();
                bytes = asset.bytes;
            }

            using (new EditorGUI.IndentLevelScope(-1))
            {
                EditorGUILayout.TextArea(prototypeCacheString);
            }
        }

        static void DebugChunk(StringBuilder builder, Prototype chunk, int nestCount, int id)
        {
            void AppendLine(string line)
            {
                for (int i = 0; i < nestCount; i++)
                {
                    builder.Append("    ");
                }

                builder.AppendLine(line);
            }

            if (nestCount == 0)
                AppendLine($"Chunk :{chunk.ChunkName}");
            else AppendLine("[" + nestCount + "," + id + "]");
            AppendLine($"Parameters:{chunk.ParameterCount}");

            AppendLine("Code -------------------------------------");
            var index = 0;
            foreach (var inst in chunk.Code)
            {
                AppendLine($"[{index}]\t{chunk.LineInfo[index]}\t\t{inst}");
                index++;
            }

            AppendLine("LocalVariables ---------------------------");
            index = 0;
            foreach (var local in chunk.LocalVariables)
            {
                AppendLine($"[{index}]\t{local.Name}\t{local.StartPc}\t{local.EndPc}");
                index++;
            }

            AppendLine("Constants ---------------------------------");
            index = 0;
            foreach (var constant in chunk.Constants.ToArray())
            {
                AppendLine($"[{index}]\t{Regex.Escape(constant.ToString())}");
                index++;
            }

            AppendLine("UpValues -----------------------------------");
            index = 0;
            foreach (var upValue in chunk.UpValues.ToArray())
            {
                AppendLine($"[{index}]\t{upValue.Name}\t{(upValue.IsLocal ? 1 : 0)}\t{upValue.Index}");
                index++;
            }

            builder.AppendLine();

            var chunkId = 0;

            foreach (var localChunk in chunk.ChildPrototypes)
            {
                DebugChunk(builder, localChunk, nestCount + 1, chunkId);
                chunkId++;
            }
        }
    }
}