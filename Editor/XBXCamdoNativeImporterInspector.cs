// Inspector structure follows EMD2Unity / BRRES2Unity Editor tab layout.
// Rig and animation are labeled unsupported, not advertised as functioning.
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.AssetImporters;
using UnityEngine;

namespace MusafirEx.XBX2Unity.Editor
{
    [CustomEditor(typeof(XBXCamdoNativeImporter))]
    public sealed class XBXCamdoNativeImporterInspector : ScriptedImporterEditor
    {
        private int selectedTab;
        private static readonly string[] Tabs = { "Model", "Rig", "Animation", "Material" };

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            selectedTab = GUILayout.Toolbar(selectedTab, Tabs);
            EditorGUILayout.Space(6);
            if (selectedTab == 0)
            {
                EditorGUILayout.HelpBox("Native CAMDO/MXMD v10040 preview. Reads positions, normals, UV and vertex colors directly, without GLB or external tools.", MessageType.Info);
                EditorGUILayout.PropertyField(serializedObject.FindProperty("modelScale"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("flipX"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("reverseFaces"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("flipUVv"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("importVertexColors"));
            }
            else if (selectedTab == 1)
            {
                EditorGUILayout.HelpBox("Rig import is not yet implemented. Static geometry only; bones, skin weights and bindposes are not created.", MessageType.Warning);
            }
            else if (selectedTab == 2)
            {
                EditorGUILayout.HelpBox("Animation import is not yet implemented. MOT and other motion files are outside the scope of v0.5.0.", MessageType.Warning);
            }
            else
            {
                EditorGUILayout.PropertyField(serializedObject.FindProperty("createPlaceholderMaterials"));
                EditorGUILayout.HelpBox("Materials are flat placeholders grouped by original material index. CASMT stage geometry is decoded, but CASMT textures and Xenoblade shaders are not decoded yet.", MessageType.Warning);
            }
            serializedObject.ApplyModifiedProperties();
            ApplyRevertGUI();
        }
    }
}
#endif
