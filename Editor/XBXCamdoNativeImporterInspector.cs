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
                EditorGUILayout.LabelField("Skell HKT Skeleton", EditorStyles.boldLabel);
                EditorGUILayout.PropertyField(serializedObject.FindProperty("importSkellHktHierarchy"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("importSkellBoneWeights"));
                if (serializedObject.FindProperty("importSkellBoneWeights").boolValue)
                    EditorGUILayout.HelpBox(
                        "Experimental Skell skinning: links MXMD WeightIndex with shared " +
                        "SkinWeights2/BoneIndices2 and matches MXMD bone names to HKT. " +
                        "Only validated meshes become SkinnedMeshRenderer; all others " +
                        "retain their original static MeshRenderer. Check Console counts.",
                        MessageType.Warning);
                if (GUILayout.Button("Audit Skell Bone Weights / MXMD Palette"))
                    XBXSkellWeightAudit.RunSelected();
                EditorGUILayout.HelpBox(
                    "For Skell CAMDO, reads the sibling *_rig.hkt and builds the native " +
                    "Havok bone hierarchy/Transform nodes. Geometry remains static until " +
                    "weight palettes, bone weights and bindposes are individually validated. " +
                    "Enable the experimental bone weights option to create skinned meshes. " +
                    "Some mesh groups may still use static renderers.",
                    MessageType.Warning);
                if (GUILayout.Button("Audit Selected Skell HKT / Resources"))
                    XBXSkellResourceAudit.RunSelected();
            }
            else if (selectedTab == 2)
            {
                EditorGUILayout.LabelField("Skell Animation Sources", EditorStyles.boldLabel);
                EditorGUILayout.HelpBox(
                    "CEA is a CES-related event/container resource; it is not " +
                    "a decoded AnimationClip. The Skell pack also contains " +
                    "parts_*, mesh_*, and effect_* CES data. Their animation " +
                    "track formats and bone bindings require additional decoding. " +
                    "No placeholder motion clips are produced.",
                    MessageType.Warning);
                if (GUILayout.Button("Audit Skell Animation Companion Files"))
                    XBXSkellResourceAudit.RunSelected();
            }
            else
            {
                EditorGUILayout.PropertyField(serializedObject.FindProperty("createPlaceholderMaterials"));
                EditorGUILayout.HelpBox(
                    "Skell chr_dl textures are MTXT GX2 surfaces in CAMDO/CASMT. " +
                    "Unlike extracted map PNGs, they are not yet deswizzled/decoded " +
                    "by this native importer. Audit the MTXT header/format before " +
                    "implementing BCn surface decompression.",
                    MessageType.Info);
                if (GUILayout.Button("Audit Skell MTXT Textures"))
                    XBXSkellResourceAudit.RunSelected();
                EditorGUILayout.Space(4);
                EditorGUILayout.LabelField("Map Texture Binding", EditorStyles.boldLabel);
                EditorGUILayout.PropertyField(serializedObject.FindProperty("bindExtractedTextures"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("baseTextureSlot"));
                EditorGUILayout.HelpBox(
                    "Automatically resolves material texture indices using MXMD external texture records. " +
                    "Looks for PNG/DDS/TGA in the extracted map textures/<container>/<id> or textures/<id>. " +
                    "Slot 0 is a preview default, not a verified Xenoblade diffuse shader slot.",
                    MessageType.Info);
                EditorGUILayout.Space(4);
                EditorGUILayout.LabelField("Experimental Edge Blending (vBlend)", EditorStyles.boldLabel);
                EditorGUILayout.PropertyField(serializedObject.FindProperty("enableExperimentalEdgeBlend"));
                if (serializedObject.FindProperty("enableExperimentalEdgeBlend").boolValue)
                {
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("blendTextureSlot"));
                    EditorGUILayout.Space(3);
                    EditorGUILayout.LabelField("Diagnostic Mask Preview", EditorStyles.boldLabel);
                    var mask = serializedObject.FindProperty("blendMaskSource");
                    mask.intValue = EditorGUILayout.Popup("Mask Source", mask.intValue,
                        new[] { "vBlend Attribute (14)", "Vertex Color RGBA", "Base Texture RGBA",
                            "Overlay Texture RGBA", "Constant Blend Weight" });
                    var channel = serializedObject.FindProperty("blendChannel");
                    channel.intValue = EditorGUILayout.Popup("Mask Channel", channel.intValue,
                        new[] { "Red", "Green", "Blue", "Alpha" });
                    var uv = serializedObject.FindProperty("overlayUVSet");
                    uv.intValue = EditorGUILayout.Popup("Overlay UV", uv.intValue,
                        new[] { "UV0", "UV1 (if present)" });
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("invertBlendMask"));
                    if (mask.intValue == 4)
                        EditorGUILayout.PropertyField(serializedObject.FindProperty("constantBlendWeight"));
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("blendThreshold"));
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("blendSoftness"));
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("useVertexTint"));
                    var view = serializedObject.FindProperty("blendDebugView");
                    view.intValue = EditorGUILayout.Popup("Debug Preview", view.intValue,
                        new[] { "Composite", "Raw Mask (grayscale)", "Blend Weight (grayscale)",
                            "Base Texture Only", "Overlay Texture Only", "vBlend RGB" });
                    EditorGUILayout.HelpBox(
                        "Start with Debug Preview = Raw Mask. If it is all black or all white, try " +
                        "another vBlend channel or a different Mask Source. Inspect the texture " +
                        "slot mapping; a second slot can be a normal/lookup map, not diffuse.",
                        MessageType.Info);
                    if (GUILayout.Button("Inspect Selected CAMDO Blend Channels"))
                        XBXCamdoBlendAudit.RunSelected();
                    EditorGUILayout.HelpBox(
                        "Original XCX terrain material layering has NOT been reconstructed. " +
                        "This mode visualizes source attributes and tests two texture layers only.",
                        MessageType.Warning);
                }
            }
            serializedObject.ApplyModifiedProperties();
            ApplyRevertGUI();
        }
    }
}
#endif
