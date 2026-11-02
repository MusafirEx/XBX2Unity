// FBX-like CAMDO material inspection, external override and extraction.
// Adapted conceptually from Bress2Unity's Materials Inspector and SAGE's
// source-material manifest. XBX MXMD indices/MTXT semantics remain authoritative.
#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace MusafirEx.XBX2Unity.Editor
{
    internal static class XBXMaterialInspectorUI
    {
        internal static void Draw(XBXCamdoNativeImporter importer, SerializedObject serialized)
        {
            if (importer == null || serialized == null) return;
            XBXMaterialManifest manifest = null;
            foreach (UnityEngine.Object item in AssetDatabase.LoadAllAssetsAtPath(importer.assetPath))
            {
                manifest = item as XBXMaterialManifest;
                if (manifest != null) break;
            }
            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("Source MXMD Materials / Unity Remapping", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(
                serialized.FindProperty("generatedMaterialShaderOverride"),
                new GUIContent("Generated Shader", "Optional override of generated material shader. Explicit shader wins over diagnostic or Lit preview."));
            EditorGUILayout.HelpBox(
                "Like BRRES/SAGE: the imported source material table is preserved. " +
                "You may assign a separate .mat per MXMD material index. " +
                "The source CAMDO/CASMT, GX2 texture indices, HKT rig and bone weights " +
                "are never modified. External materials are applied after native previews. " +
                "For editing shader properties, Extract Materials first.",
                MessageType.Info);
            if (manifest == null)
            {
                EditorGUILayout.HelpBox(
                    "No MXMD material manifest found. Click Apply/Reimport to generate one.",
                    MessageType.Info);
                return;
            }
            EditorGUILayout.LabelField("Source materials: " + manifest.entries.Count,
                EditorStyles.miniBoldLabel);
            if (GUILayout.Button("Export RGB Companion Channel Audit (.txt)"))
                ExportRgbAudit(importer, manifest);

            if (GUILayout.Button("Extract All Generated Materials as .mat and Assign Overrides"))
            {
                Extract(importer, serialized, manifest, -1);
                return;
            }

            SerializedProperty list = serialized.FindProperty("materialOverrides");
            if (list == null) return;
            int shown = 0;
            foreach (XBXMaterialEntry entry in manifest.entries)
            {
                if (entry == null || shown++ >= 160) break;
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        EditorGUILayout.LabelField("MXMD [" +
                            entry.materialIndex.ToString("D4") + "] " + entry.sourceName,
                            EditorStyles.boldLabel);
                        if (entry.generatedMaterial != null &&
                            GUILayout.Button("Extract", GUILayout.Width(72)))
                        {
                            Extract(importer, serialized, manifest, entry.materialIndex);
                            return;
                        }
                    }
                    EditorGUILayout.LabelField("Renderers: " + entry.rendererCount +
                        " | Base: " + entry.chosenAlbedoSlot +
                        " | Normal: " + entry.normalCandidateSlot +
                        " | SPM: " + entry.specularCandidateSlot,
                        EditorStyles.miniLabel);
                    EditorGUILayout.LabelField("Reflection: " + entry.reflectionCandidateSlot +
                        " | Glow: " + entry.glowCandidateSlot +
                        " | RGB Companion: " + entry.rgbCompanionSlot,
                        EditorStyles.miniLabel);
                    EditorGUILayout.LabelField(entry.bindingStatus, EditorStyles.miniLabel);
                    using (new EditorGUI.DisabledScope(true))
                    {
                        EditorGUILayout.ObjectField("Imported Preview",
                            entry.generatedMaterial, typeof(Material), false);
                    }
                    SerializedProperty target = Find(list, entry.materialIndex);
                    Material current = target == null ? null :
                        target.FindPropertyRelative("material").objectReferenceValue as Material;
                    EditorGUI.BeginChangeCheck();
                    Material replacement = EditorGUILayout.ObjectField("External .mat Override",
                        current, typeof(Material), false) as Material;
                    if (EditorGUI.EndChangeCheck())
                    {
                        if (replacement != null && !ValidExternal(importer, replacement))
                            EditorUtility.DisplayDialog("External Material Required",
                                "Choose a .mat asset located under Assets/, not an imported CAMDO sub-asset. " +
                                "Use Extract first to create an editable material.", "OK");
                        else SetOverride(list, entry.materialIndex, replacement);
                    }
                    EditorGUILayout.LabelField("MXMD gMatCol: " + entry.sourceColor,
                        EditorStyles.miniLabel);
                    if (entry.textureSlots == null || entry.textureSlots.Count == 0)
                        EditorGUILayout.LabelField("No native MXMD texture links.", EditorStyles.miniLabel);
                    else foreach (XBXMaterialTextureSlot slot in entry.textureSlots)
                    {
                        using (new EditorGUILayout.HorizontalScope())
                        {
                            string label = "slot " + slot.slot + " / MTXT " + slot.textureId;
                            EditorGUILayout.LabelField(label, GUILayout.Width(122));
                            EditorGUILayout.LabelField(
                                (string.IsNullOrEmpty(slot.sourceName) ? "(not decoded)" : slot.sourceName) +
                                "  [" + slot.decodedFormat + ", usage " + slot.usageHint +
                                " | " + (slot.inferredRole ?? "Unknown") + "]",
                                EditorStyles.miniLabel);
                        }
                        if (!string.IsNullOrEmpty(slot.sourceName) &&
                            slot.sourceName.EndsWith("_RGB", StringComparison.OrdinalIgnoreCase) &&
                            !string.IsNullOrEmpty(slot.bc1ChannelProfile))
                            EditorGUILayout.LabelField("BC1 RGB source channel sample: " +
                                slot.bc1ChannelProfile, EditorStyles.wordWrappedMiniLabel);
                    }
                }
            }
            if (shown >= 160)
                EditorGUILayout.HelpBox(
                    "Showing up to 160 MXMD source material entries for Inspector responsiveness.",
                    MessageType.Info);
        }

        private static void ExportRgbAudit(XBXCamdoNativeImporter importer,
            XBXMaterialManifest manifest)
        {
            string defaultName = Path.GetFileNameWithoutExtension(importer.assetPath) +
                "_rgb_channel_audit";
            string destination = EditorUtility.SaveFilePanel(
                "Export MXMD RGB Channel Audit", "", defaultName, "txt");
            if (string.IsNullOrEmpty(destination)) return;
            var lines = new System.Text.StringBuilder();
            lines.AppendLine("XBX2Unity MXMD RGB Companion Source Channel Audit");
            lines.AppendLine("Source CAMDO: " + importer.assetPath);
            lines.AppendLine("BC1 analysis is a sampled color distribution, not proof of paint-mask semantics.");
            lines.AppendLine("BC1 alpha is either fully opaque or binary 0/255: NOT a fourth gradient zone.");
            lines.AppendLine();
            int found = 0;
            foreach (XBXMaterialEntry entry in manifest.entries)
            {
                if (entry == null || entry.textureSlots == null) continue;
                foreach (XBXMaterialTextureSlot slot in entry.textureSlots)
                {
                    if (slot == null || string.IsNullOrEmpty(slot.sourceName) ||
                        !slot.sourceName.EndsWith("_RGB", StringComparison.OrdinalIgnoreCase))
                        continue;
                    found++;
                    lines.Append("MXMD material ").Append(entry.materialIndex)
                        .Append(" slot ").Append(slot.slot).Append(" textureId ")
                        .Append(slot.textureId).Append(" name ").AppendLine(slot.sourceName);
                    lines.AppendLine(slot.bc1ChannelProfile ?? "Channel profile unavailable");
                    lines.AppendLine();
                }
            }
            lines.AppendLine("Companion references: " + found);
            try
            {
                File.WriteAllText(destination, lines.ToString());
                EditorUtility.RevealInFinder(destination);
                Debug.Log("XBX RGB source channel audit exported to " + destination);
            }
            catch (Exception ex)
            {
                EditorUtility.DisplayDialog("Export Failed", ex.Message, "OK");
            }
        }

        private static SerializedProperty Find(SerializedProperty list, int materialIndex)
        {
            if (list == null || !list.isArray) return null;
            for (int i = 0; i < list.arraySize; i++)
            {
                var item = list.GetArrayElementAtIndex(i);
                if (item.FindPropertyRelative("materialIndex").intValue == materialIndex)
                    return item;
            }
            return null;
        }

        private static void SetOverride(SerializedProperty list, int index, Material material)
        {
            if (list == null || !list.isArray) return;
            var item = Find(list, index);
            if (item == null)
            {
                if (material == null) return;
                int i = list.arraySize;
                list.InsertArrayElementAtIndex(i);
                item = list.GetArrayElementAtIndex(i);
                item.FindPropertyRelative("materialIndex").intValue = index;
            }
            item.FindPropertyRelative("material").objectReferenceValue = material;
        }

        private static bool ValidExternal(XBXCamdoNativeImporter importer, Material material)
        {
            if (material == null) return false;
            string p = AssetDatabase.GetAssetPath(material);
            return !string.IsNullOrEmpty(p) && p.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase) &&
                p.EndsWith(".mat", StringComparison.OrdinalIgnoreCase) &&
                p != importer.assetPath;
        }

        private static void Extract(XBXCamdoNativeImporter importer, SerializedObject serialized,
            XBXMaterialManifest manifest, int specificIndex)
        {
            string source = importer.assetPath.Replace('\\', '/');
            if (!source.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
            {
                EditorUtility.DisplayDialog("Cannot Extract",
                    "The CAMDO must be inside Assets/ to create editable external .mat files.", "OK");
                return;
            }
            string parent = Path.GetDirectoryName(source).Replace('\\', '/');
            string folderName = Path.GetFileNameWithoutExtension(source) + "_Materials";
            string folder = parent + "/" + folderName;
            if (!AssetDatabase.IsValidFolder(folder))
                AssetDatabase.CreateFolder(parent, folderName);
            if (!AssetDatabase.IsValidFolder(folder))
            {
                EditorUtility.DisplayDialog("Cannot Extract",
                    "Unity could not create the materials directory: " + folder, "OK");
                return;
            }
            SerializedProperty list = serialized.FindProperty("materialOverrides");
            if (list == null) return;
            int created = 0;
            foreach (var entry in manifest.entries)
            {
                if (entry == null || entry.generatedMaterial == null) continue;
                if (specificIndex >= 0 && entry.materialIndex != specificIndex) continue;
                // Don't replace an existing editable external .mat silently.
                SerializedProperty existing = Find(list, entry.materialIndex);
                Material assigned = existing == null ? null :
                    existing.FindPropertyRelative("material").objectReferenceValue as Material;
                if (assigned != null && ValidExternal(importer, assigned))
                    continue;
                string safeName = "MXMD_" + entry.materialIndex.ToString("D4");
                string path = AssetDatabase.GenerateUniqueAssetPath(folder + "/" + safeName + ".mat");
                Material clone = new Material(entry.generatedMaterial);
                clone.name = safeName;
                AssetDatabase.CreateAsset(clone, path);
                SetOverride(list, entry.materialIndex, clone);
                created++;
            }
            if (created == 0)
            {
                EditorUtility.DisplayDialog("No Materials Extracted",
                    "No new materials were required, or material generation is disabled. " +
                    "Existing external overrides were preserved.", "OK");
                return;
            }
            serialized.ApplyModifiedProperties();
            AssetDatabase.SaveAssets();
            EditorUtility.SetDirty(importer);
            importer.SaveAndReimport();
            GUIUtility.ExitGUI();
        }
    }
}
#endif
