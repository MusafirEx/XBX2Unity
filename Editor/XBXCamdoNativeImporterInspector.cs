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
                EditorGUILayout.Space(5);
                EditorGUILayout.LabelField("Skell MTXT / GX2 Native Preview", EditorStyles.boldLabel);
                EditorGUILayout.PropertyField(serializedObject.FindProperty("importSkellMtxtTextures"));
                if (serializedObject.FindProperty("importSkellMtxtTextures").boolValue)
                {
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("maxSkellTextures"));
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("autoSelectSkellColorTexture"));
                    if (serializedObject.FindProperty("autoSelectSkellColorTexture").boolValue)
                        EditorGUILayout.PropertyField(
                            serializedObject.FindProperty("preferSkellRgbCompanionAsAlbedo"),
                            new GUIContent("Prefer Skell _RGB as Albedo",
                                "OFF = source primary texture before *_RGB. ON = experimental *_RGB as base color, useful to compare custom Skell paint data."));
                    if (!serializedObject.FindProperty("autoSelectSkellColorTexture").boolValue)
                        EditorGUILayout.HelpBox(
                            "Manual mode uses Base Texture Slot below. A source " +
                            "BC5/NRM texture will NEVER be assigned as base color.",
                            MessageType.Warning);
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("useSkellMaterialColor"));
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("flipUVv"),
                        new GUIContent("Flip V on CAMDO UVs",
                            "Optional original Wii U vs Unity texture vertical axis test; also available in Model tab."));
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("skellColorDiagnosticShader"));
                    if (serializedObject.FindProperty("skellColorDiagnosticShader").boolValue)
                    {
                        EditorGUILayout.PropertyField(serializedObject.FindProperty("skellUseVertexColor"));
                        var debug = serializedObject.FindProperty("skellDebugView");
                        debug.intValue = EditorGUILayout.Popup("Skell Debug View",
                            debug.intValue, new[] {
                                "Composite (texture x material x optional vertex)",
                                "Raw Color Texture",
                                "Source Vertex Color",
                                "Source gMatCol",
                                "UV0 Gradient (no texture)",
                                "UV0 Checkerboard (no texture)" });
                    }
                    EditorGUILayout.HelpBox(
                        "Auto mode uses source MXMD texture metadata. Unsuffixed " +
                        "primary BC1 is preferred over *_RGB (possibly custom paint data). " +
                        "Toggle Prefer Skell _RGB as Albedo for comparisons. " +
                        "BC5/NRM data are not placed " +
                        "in the albedo slot. Source gMatCol is optional. The diagnostic " +
                        "shader can compare source texture and raw mesh UV0 (checker). " +
                        "Use UV0 Checkerboard to separate broken UVs from wrong GX2 deswizzle. " +
                        "Check the Console material-to-texture audit after Reimport. " +
                        "The real Skell multi-pass shader is still unknown.",
                        MessageType.Info);
                }
                else
                    EditorGUILayout.HelpBox(
                        "Enable Import Skell MTXT Textures to read BC1/BC5 base mips " +
                        "directly from CAMDO/CASMT.", MessageType.Info);
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
                EditorGUILayout.Space(8);
                EditorGUILayout.LabelField("Skell Material Layers (Experimental)", EditorStyles.boldLabel);
                EditorGUILayout.PropertyField(serializedObject.FindProperty("importSkellNormalMaps"),
                    new GUIContent("Bind BC5 Normal Candidates",
                        "From source per-material NRM slots; generated materials only, not external .mat."));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("importSkellSpecularMaps"),
                    new GUIContent("Bind SPM Specular Candidates",
                        "From source SPM slots; requires a compatible shader and does not touch external .mat."));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("skellLayerPreviewShader"),
                    new GUIContent("Use Skell Layer Preview Shader",
                        "Diagnostic approximation only, not original Wii U lighting/paint pipeline."));
                if (serializedObject.FindProperty("skellLayerPreviewShader").boolValue)
                {
                    var view = serializedObject.FindProperty("skellLayerDebugView");
                    view.intValue = EditorGUILayout.Popup("Layer Debug View",
                        view.intValue, new[] { "Composite", "Raw Base Color",
                            "BC5 Normal Candidate", "SPM Specular Candidate",
                            "RGBA RGB Companion Candidate" });
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("skellUseRgbCompanionAsPaintMask"),
                        new GUIContent("Interpret _RGB as Paint Mask (Unverified)",
                            "OFF by default. Source *_RGB may not be an RGBA paint-zone mask."));
                    if (serializedObject.FindProperty("skellUseRgbCompanionAsPaintMask").boolValue)
                    {
                        EditorGUILayout.PropertyField(serializedObject.FindProperty("skellPaintStrength"));
                        EditorGUILayout.PropertyField(serializedObject.FindProperty("skellPaintColor1"));
                        EditorGUILayout.PropertyField(serializedObject.FindProperty("skellPaintColor2"));
                        EditorGUILayout.PropertyField(serializedObject.FindProperty("skellPaintColor3"));
                        EditorGUILayout.PropertyField(serializedObject.FindProperty("skellPaintColor4"));
                    }
                }
                EditorGUILayout.HelpBox(
                    "Role candidates use MXMD texture IDs and source names, not guessed " +
                    "textures from another Skell. RFM/REF and glow roles are reported " +
                    "but not assigned as specular/albedo. Paint mask interpretation is " +
                    "an explicit experiment, OFF by default. Existing external .mat " +
                    "overrides always take precedence; the importer does not overwrite them. " +
                    "Use generated material previews to test new source bindings.",
                    MessageType.Info);
                XBXMaterialInspectorUI.Draw((XBXCamdoNativeImporter)target, serializedObject);
            }
            serializedObject.ApplyModifiedProperties();
            ApplyRevertGUI();
        }
    }
}
#endif
