// Dedicated inspectors for HKT/SAR native import settings.
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.AssetImporters;
using UnityEngine;

namespace MusafirEx.XBX2Unity.Editor
{
    [CustomEditor(typeof(XBXSkellHktAssetImporter))]
    public sealed class XBXSkellHktAssetImporterInspector : ScriptedImporterEditor
    {
        public override void OnInspectorGUI()
        {
            EditorGUILayout.HelpBox(
                "Native HKT: no manual .anim conversion. The imported HKT asset " +
                "becomes a real AnimationClip with binding paths rooted at the " +
                "CAMDO model (Skell_Rig/...). A *_rig.hkt is kept as a skeleton, " +
                "never as an animation. Matching CAMDO and sibling *_rig.hkt " +
                "must exist inside Assets. For a compatible Skell such as " +
                "dl099100 -> dl099110, add its CAMDO under Compatible Skell " +
                "CAMDOs. The importer generates a separate rooted clip.",
                MessageType.Info);
            serializedObject.Update();
            EditorGUILayout.PropertyField(serializedObject.FindProperty("skellCamdo"),
                new GUIContent("Skell CAMDO"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("autoFindSkellCamdo"),
                new GUIContent("Auto-find matching CAMDO"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("compatibleSkellCamdos"),
                new GUIContent("Compatible Skell CAMDOs"), true);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("loopSeamlessIdle"),
                new GUIContent("Auto Loop Seamless Idle"));
            serializedObject.ApplyModifiedProperties();
            ApplyRevertGUI();
        }
    }

    [CustomEditor(typeof(XBXSkellSarAssetImporter))]
    public sealed class XBXSkellSarAssetImporterInspector : ScriptedImporterEditor
    {
        public override void OnInspectorGUI()
        {
            EditorGUILayout.HelpBox(
                "Native SAR animation container: each supported HKT motion " +
                "appears as a Unity AnimationClip child asset. No .hkt extraction " +
                "and no .anim conversion are required. Expand the SAR asset " +
                "in Project, then drag clips into an Animator Controller. " +
                "Add another model to Compatible Skell CAMDOs to create " +
                "additional Skell-rooted variants from the same SAR without " +
                "copying source files. Each target must pass strict bone-map validation.",
                MessageType.Info);
            serializedObject.Update();
            EditorGUILayout.PropertyField(serializedObject.FindProperty("skellCamdo"),
                new GUIContent("Skell CAMDO"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("autoFindSkellCamdo"),
                new GUIContent("Auto-find matching CAMDO"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("compatibleSkellCamdos"),
                new GUIContent("Compatible Skell CAMDOs"), true);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("loopSeamlessIdle"),
                new GUIContent("Auto Loop Seamless Idle"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("nameContains"),
                new GUIContent("Animation Name Filter"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("maxClips"),
                new GUIContent("Max Clips (0 = All)"));
            serializedObject.ApplyModifiedProperties();
            EditorGUILayout.HelpBox(
                "Large Skell SAR archives can contain hundreds of motions. " +
                "Set a name filter or Max Clips during development for faster " +
                "imports. Source and compatible clip variants each have their " +
                "own model root path. Unsupported codecs or bone mismatches " +
                "are skipped; see the container manifest for details.",
                MessageType.None);
            ApplyRevertGUI();
        }
    }
}
#endif
