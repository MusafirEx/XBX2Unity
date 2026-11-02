// Reports which imported meshes actually carry source vBlend data.
// This is read-only, and does not modify the CAMDO, CASMT, or Unity assets.
#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace MusafirEx.XBX2Unity.Editor
{
    internal static class XBXCamdoBlendAudit
    {
        [MenuItem("Assets/XBX2Unity/Inspect CAMDO Blend Data", false, 2104)]
        [MenuItem("Tools/XBX2Unity/Inspect Selected CAMDO Blend Data", false, 160)]
        internal static void RunSelected()
        {
            string path = AssetDatabase.GetAssetPath(Selection.activeObject);
            if (string.IsNullOrEmpty(path) ||
                !path.EndsWith(".camdo", StringComparison.OrdinalIgnoreCase))
            {
                EditorUtility.DisplayDialog("CAMDO Blend Audit",
                    "Select a CAMDO model in the Unity Project window.", "OK");
                return;
            }

            try
            {
                var root = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (root == null)
                    throw new InvalidOperationException("CAMDO has no imported root GameObject.");

                MeshFilter[] filters = root.GetComponentsInChildren<MeshFilter>(true);
                const int limit = 160;
                int tested = 0, withBlend = 0, variableBlend = 0, withColor = 0, withUv1 = 0;
                var report = new StringBuilder();
                report.AppendLine("XBX2Unity v0.6.2 Blend Audit");
                report.AppendLine("Source: " + path);
                report.AppendLine("MeshFilter objects: " + filters.Length);
                report.AppendLine("Sample limit: " + limit);
                report.AppendLine("vBlend is MXMD attribute type 14, stored in Mesh.uv3 (TEXCOORD2).");
                report.AppendLine();

                var uv3 = new List<Vector4>();
                var seen = new HashSet<Mesh>();
                foreach (MeshFilter filter in filters)
                {
                    if (tested >= limit) break;
                    Mesh mesh = filter.sharedMesh;
                    if (mesh == null || !seen.Add(mesh)) continue;
                    tested++;
                    bool hasColor = mesh.HasVertexAttribute(VertexAttribute.Color);
                    bool hasUv1 = mesh.HasVertexAttribute(VertexAttribute.TexCoord1);
                    if (hasColor) withColor++;
                    if (hasUv1) withUv1++;
                    uv3.Clear();
                    if (mesh.HasVertexAttribute(VertexAttribute.TexCoord2))
                        mesh.GetUVs(2, uv3);

                    Vector4 min = new Vector4(1, 1, 1, 1);
                    Vector4 max = Vector4.zero;
                    if (uv3.Count > 0)
                    {
                        withBlend++;
                        foreach (Vector4 v in uv3)
                        {
                            min = Vector4.Min(min, v);
                            max = Vector4.Max(max, v);
                        }
                        if ((max - min).sqrMagnitude > 0.0001f) variableBlend++;
                    }

                    report.Append("Mesh ").Append(tested).Append(": ")
                        .Append(mesh.name).Append(" vertexCount=").Append(mesh.vertexCount)
                        .Append(" vBlend=").Append(uv3.Count > 0 ? "yes" : "no")
                        .Append(" vColor=").Append(hasColor ? "yes" : "no")
                        .Append(" UV1=").Append(hasUv1 ? "yes" : "no");
                    if (uv3.Count > 0)
                    {
                        report.Append(" min=").Append(Format(min))
                            .Append(" max=").Append(Format(max));
                    }
                    report.AppendLine();
                }

                report.AppendLine();
                report.AppendLine("Sampled unique meshes: " + tested);
                report.AppendLine("With vBlend (UV3): " + withBlend);
                report.AppendLine("With varying vBlend: " + variableBlend);
                report.AppendLine("With vertex color: " + withColor);
                report.AppendLine("With second UV (UV1): " + withUv1);
                report.AppendLine();
                report.AppendLine("Materials (first 24):");
                int materialCount = 0;
                foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(path))
                {
                    Material material = asset as Material;
                    if (material == null) continue;
                    materialCount++;
                    if (materialCount > 24) continue;
                    report.Append("  ").Append(material.name)
                        .Append(" shader=").Append(material.shader != null ? material.shader.name : "NULL")
                        .Append(" base=").Append(NameOfTexture(material, "_MainTex"))
                        .Append(" overlay=").Append(NameOfTexture(material, "_BlendTex"));
                    if (material.HasProperty("_EnableBlend"))
                        report.Append(" enabled=").Append(material.GetFloat("_EnableBlend"));
                    if (material.HasProperty("_MaskSource"))
                        report.Append(" maskSource=").Append(material.GetFloat("_MaskSource"));
                    report.AppendLine();
                }
                report.AppendLine("Total material sub-assets: " + materialCount);
                report.AppendLine();
                report.AppendLine("If vBlend is absent, test vertex color or texture alpha in Inspector.");
                report.AppendLine("If vBlend min=max across a mesh, that channel cannot show an edge transition.");
                report.AppendLine("Material texture slot 1 is NOT guaranteed to be an overlay albedo.");
                report.AppendLine("The output is a diagnostic only; original Xenoblade shader behavior is unverified.");

                string projectRoot = Directory.GetParent(Application.dataPath).FullName;
                string folder = Path.Combine(projectRoot, "XBX_Exports", "BlendAudits");
                Directory.CreateDirectory(folder);
                string file = path.Replace("Assets/", "").Replace('/', '_')
                    .Replace('\\', '_') + ".blend.txt";
                string output = Path.Combine(folder, file);
                File.WriteAllText(output, report.ToString(), Encoding.UTF8);
                Debug.Log("[XBX2Unity] CAMDO blend audit saved: " + output + "\n" +
                    "Meshes checked=" + tested + ", vBlend=" + withBlend +
                    ", varying=" + variableBlend + ", vertexColor=" + withColor +
                    ", UV1=" + withUv1 + ", materials=" + materialCount);
                EditorUtility.DisplayDialog("CAMDO Blend Audit",
                    "Report saved to:\n" + output + "\n\n" +
                    "vBlend: " + withBlend + "/" + tested +
                    "\nVarying vBlend: " + variableBlend +
                    "\nVertex color: " + withColor +
                    "\nUV1: " + withUv1, "OK");
            }
            catch (Exception e)
            {
                Debug.LogError("[XBX2Unity] CAMDO blend audit failed: " + e);
                EditorUtility.DisplayDialog("CAMDO Blend Audit Failed", e.Message, "OK");
            }
        }

        [MenuItem("Assets/XBX2Unity/Inspect CAMDO Blend Data", true)]
        private static bool ValidateAsset()
        {
            string path = AssetDatabase.GetAssetPath(Selection.activeObject);
            return !string.IsNullOrEmpty(path) &&
                   path.EndsWith(".camdo", StringComparison.OrdinalIgnoreCase);
        }

        private static string NameOfTexture(Material material, string property)
        {
            if (!material.HasProperty(property)) return "-";
            Texture texture = material.GetTexture(property);
            return texture == null ? "-" : texture.name;
        }

        private static string Format(Vector4 v)
        {
            return "(" + v.x.ToString("F2") + "," + v.y.ToString("F2") +
                "," + v.z.ToString("F2") + "," + v.w.ToString("F2") + ")";
        }
    }
}
#endif
