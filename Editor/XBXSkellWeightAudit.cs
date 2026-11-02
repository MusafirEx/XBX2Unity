// Read-only weight map audit for Xenoblade X CAMDO + HKT.
// Does not fabricate weights, edit game files, or create AnimationClips.
#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace MusafirEx.XBX2Unity.Editor
{
    internal static class XBXSkellWeightAudit
    {
        [MenuItem("Assets/XBX2Unity/Audit Skell Bone Weights", false, 2106)]
        [MenuItem("Tools/XBX2Unity/Audit Selected Skell Bone Weights", false, 171)]
        internal static void RunSelected()
        {
            string path = AssetDatabase.GetAssetPath(Selection.activeObject);
            if (string.IsNullOrEmpty(path) ||
                !path.EndsWith(".camdo", StringComparison.OrdinalIgnoreCase))
            {
                EditorUtility.DisplayDialog("Skell Bone Weight Audit",
                    "Select a Skell .camdo in the Unity Project window.", "OK");
                return;
            }
            try
            {
                var reader = new LegacyCamdoReader(File.ReadAllBytes(path));
                string casmt = Path.ChangeExtension(path, ".casmt");
                var doc = reader.Parse(File.Exists(casmt) ? casmt : null);
                string hktPath = Path.Combine(Path.GetDirectoryName(path) ?? "",
                    Path.GetFileNameWithoutExtension(path) + "_rig.hkt");
                XBXSkellHktRig.Skeleton rig = File.Exists(hktPath)
                    ? XBXSkellHktRig.Read(hktPath) : null;
                var output = new StringBuilder();
                output.AppendLine("XBX2Unity Skell Bone Weight Audit v0.7.1");
                output.AppendLine("CAMDO: " + path);
                output.AppendLine("MXMD model groups: " + doc.models.Count);
                output.AppendLine("HKT bones: " + (rig == null ? 0 : rig.bones.Count));
                output.AppendLine("MXMD skin palette names: " + doc.skinBoneNames.Count);
                output.AppendLine("MXMD palette warning: " + (doc.skinMetadataWarning ?? "-"));
                output.AppendLine();

                var names = new HashSet<string>(StringComparer.Ordinal);
                if (rig != null) foreach (var b in rig.bones) names.Add(b.name);
                int absent = 0;
                for (int i = 0; i < doc.skinBoneNames.Count; i++)
                {
                    bool matched = names.Contains(doc.skinBoneNames[i]);
                    if (!matched) absent++;
                    if (i < 256)
                        output.Append("Palette ").Append(i.ToString("D3")).Append(": ")
                            .Append(doc.skinBoneNames[i]).Append(matched ? " MATCH" : " NOT_IN_HKT")
                            .AppendLine();
                }
                output.AppendLine("MXMD names absent from HKT: " + absent);
                output.AppendLine();

                var seen = new HashSet<LegacyCamdoReader.Geometry>();
                int withWeightIndices = 0, sharedSkinBuffers = 0, totalDraws = 0;
                foreach (var model in doc.models)
                {
                    totalDraws += model.meshes.Count;
                    var geo = model.geometry;
                    if (geo == null || !seen.Add(geo)) continue;
                    output.AppendLine("=== GEOMETRY #" + seen.Count + " ===");
                    output.Append("Weight buffer header slots: ");
                    foreach (ushort idx in geo.weightBufferIndices)
                        output.Append(idx).Append(' ');
                    output.AppendLine();
                    for (int i = 0; i < geo.vertices.Count; i++)
                    {
                        var vb = geo.vertices[i];
                        var index = vb.Find(3, 4);
                        var weights = vb.Find(1);
                        var joints = vb.Find(2);
                        if (index != null) withWeightIndices++;
                        if (weights != null && joints != null) sharedSkinBuffers++;
                        output.Append("Buffer ").Append(i)
                            .Append(" verts=").Append(vb.count)
                            .Append(" stride=").Append(vb.stride).Append(" attributes=");
                        foreach (var a in vb.attributes)
                            output.Append(a.type).Append('(').Append(a.size).Append(") ");
                        output.AppendLine();
                        if (index != null && index.size >= 4 && vb.count > 0)
                        {
                            int min = int.MaxValue, max = 0;
                            int n = Math.Min(vb.count, 10000);
                            for (int v = 0; v < n; v++)
                            {
                                int a = vb.dataOffset + v * vb.stride + index.offset;
                                int wi = geo.reader.U16(a);
                                min = Math.Min(min, wi);
                                max = Math.Max(max, wi);
                            }
                            output.AppendLine("  WeightIndex sample " + n +
                                " min=" + min + " max=" + max);
                        }
                    }
                }
                int reportedGroups = 0;
                foreach (var model in doc.models)
                {
                    if (reportedGroups++ >= 32) break;
                    output.AppendLine("Group " + (reportedGroups - 1) +
                        " drawCount=" + model.meshes.Count);
                    for (int i = 0; i < Math.Min(model.meshes.Count, 24); i++)
                    {
                        var draw = model.meshes[i];
                        output.Append("  draw ").Append(i).Append(" flags2=0x")
                            .Append(draw.flags2.ToString("X8")).Append(" vertexBuffer=")
                            .Append(draw.vertexBufferIndex).Append(" indexBuffer=")
                            .Append(draw.indexBufferIndex).AppendLine();
                    }
                }
                output.AppendLine();
                output.AppendLine("Distinct geometry headers: " + seen.Count);
                output.AppendLine("Total model draw calls: " + totalDraws);
                output.AppendLine("Vertex buffers with WeightIndex (type 3/4): " + withWeightIndices);
                output.AppendLine("Shared weight buffers with SkinWeights2 (1) + BoneIndices2 (2): " +
                    sharedSkinBuffers);
                output.AppendLine("This is a source audit, NOT proof of correct bone skinning.");
                output.AppendLine("Reference: ScanMountGoat/xc3_lib xc3_model/src/skinning.rs.");

                string root = Directory.GetParent(Application.dataPath).FullName;
                string folder = Path.Combine(root, "XBX_Exports", "SkellAudits");
                Directory.CreateDirectory(folder);
                string filename = Path.GetFileNameWithoutExtension(path) + "_skin_weights.txt";
                string reportPath = Path.Combine(folder, filename);
                File.WriteAllText(reportPath, output.ToString(), Encoding.UTF8);
                Debug.Log("[XBX2Unity] Skell bone weight audit saved: " + reportPath +
                    "\nMXMD palette=" + doc.skinBoneNames.Count +
                    ", HKT missing names=" + absent +
                    ", WeightIndex buffers=" + withWeightIndices +
                    ", shared skin buffers=" + sharedSkinBuffers);
                EditorUtility.DisplayDialog("Skell Bone Weight Audit",
                    "Source mapping report saved:\n" + reportPath +
                    "\n\nMXMD palette names: " + doc.skinBoneNames.Count +
                    "\nMissing from HKT: " + absent +
                    "\nWeightIndex buffers: " + withWeightIndices +
                    "\nShared skin buffers: " + sharedSkinBuffers, "OK");
            }
            catch (Exception e)
            {
                Debug.LogError("[XBX2Unity] Skell bone weight audit failed: " + e);
                EditorUtility.DisplayDialog("Skell Bone Weight Audit Failed", e.Message, "OK");
            }
        }

        [MenuItem("Assets/XBX2Unity/Audit Skell Bone Weights", true)]
        private static bool Validate()
        {
            string path = AssetDatabase.GetAssetPath(Selection.activeObject);
            return !string.IsNullOrEmpty(path) &&
                path.EndsWith(".camdo", StringComparison.OrdinalIgnoreCase);
        }
    }
}
#endif
