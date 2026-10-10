using UnityEditor;

namespace Lua.Unity.Editor
{
    [CustomEditor(typeof(LuaAsset))]
    public sealed class LuaAssetEditor : UnityEditor.Editor
    {
        SerializedProperty textProperty;

        public override void OnInspectorGUI()
        {
            if (textProperty == null)
                textProperty = serializedObject.FindProperty("text");

            using (new EditorGUI.IndentLevelScope(-1))
            {
                EditorGUILayout.TextArea(textProperty.stringValue);
            }
        }
    }
}
