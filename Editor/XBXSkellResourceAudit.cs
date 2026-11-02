// Read-only companion analysis for original Wii U Skell assets. No generated
// keyframes, fabricated textures or modifications to original game resources.
#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace MusafirEx.XBX2Unity.Editor
{
    internal static class XBXSkellResourceAudit
    {
        [MenuItem("Assets/XBX2Unity/Audit Skell Textures Rig Animation", false, 2105)]
        [MenuItem("Tools/XBX2Unity/Audit Selected Skell Resources", false, 170)]
        internal static void RunSelected()
        {
            string selected = AssetDatabase.GetAssetPath(Selection.activeObject);
            if (string.IsNullOrEmpty(selected) ||
                !selected.EndsWith(".camdo", StringComparison.OrdinalIgnoreCase))
            {
                EditorUtility.DisplayDialog("Skell Resource Audit",
                    "Select a Skell .camdo asset in the Project window.", "OK");
                return;
            }
            try
            {
                string basePath = Path.ChangeExtension(selected, null);
                string folder = Path.GetDirectoryName(selected);
                string name = Path.GetFileNameWithoutExtension(selected);
                var report = new StringBuilder();
                report.AppendLine("XBX2Unity v0.7.0 Skell Native Resource Audit");
                report.AppendLine("Source: " + selected);
                report.AppendLine("Original files are READ ONLY; no animation clips or textures generated.");
                report.AppendLine();
                string camdo = basePath + ".camdo";
                string casmt = basePath + ".casmt";
                string camtp = basePath + ".camtp";
                string rig = basePath + "_rig.hkt";
                string cea = basePath + ".cea";
                string efp = basePath + ".efp";
                foreach (string file in new[] { camdo, casmt, camtp, rig, cea, efp })
                    AppendFileHeader(report, file);

                report.AppendLine();
                report.AppendLine("Havok rig hierarchy:");
                if (File.Exists(rig))
                {
                    try
                    {
                        var skeleton = XBXSkellHktRig.Read(rig);
                        report.AppendLine("Parsed HKT skeleton: " + skeleton.bones.Count + " bones");
                        report.AppendLine("HKT root label: " + skeleton.rootName);
                        for (int i = 0; i < skeleton.bones.Count; i++)
                        {
                            var b = skeleton.bones[i];
                            report.Append(i.ToString("D3")).Append(" parent=")
                                .Append(b.parent).Append(" name=").Append(b.name)
                                .AppendLine();
                        }
                    }
                    catch (Exception e) { report.AppendLine("HKT PARSE WARNING: " + e.Message); }
                }
                else report.AppendLine("No sibling *_rig.hkt present");

                report.AppendLine();
                report.AppendLine("MTXT footer scan (metadata, not deswizzled images):");
                AppendMtxtReport(report, camdo);
                AppendMtxtReport(report, casmt);

                report.AppendLine();
                report.AppendLine("Associated event/script resources:");
                if (Directory.Exists(folder))
                {
                    string[] cesFiles = Directory.GetFiles(folder, "*.ces", SearchOption.TopDirectoryOnly);
                    int found = 0;
                    foreach (string file in cesFiles.OrderBy(f => f, StringComparer.Ordinal))
                    {
                        string n = Path.GetFileName(file);
                        if (!n.Contains(name) && !n.Equals("effect_dl.ces", StringComparison.OrdinalIgnoreCase))
                            continue;
                        AppendFileHeader(report, file);
                        found++;
                    }
                    report.AppendLine("Associated CES files: " + found);
                    if (File.Exists(Path.Combine(folder, "clipevt_dl.cea")))
                        AppendFileHeader(report, Path.Combine(folder, "clipevt_dl.cea"));
                }
                report.AppendLine();
                report.AppendLine("Interpretation:");
                report.AppendLine("- HKT = Havok skeleton hierarchy/TRS (not a complete skinned mesh or animation).");
                report.AppendLine("- MTXT = Wii U GX2 texture surface; requires deswizzle and BCn decode.");
                report.AppendLine("- CAMTP = small binary companion; exact per-model role not yet verified.");
                report.AppendLine("- CEA/CES = event/container resources; no bone keyframes assumed.");
                report.AppendLine("- No unsupported model data is silently interpreted as motion tracks.");
                report.AppendLine("References: ScanMountGoat/xc3_lib hkt.rs, mtxt.rs, mxmd/legacy.rs;");
                report.AppendLine("PredatorCZ/XenoLib/source/MXMD_V1.h.");
                string projectRoot = Directory.GetParent(Application.dataPath).FullName;
                string outputDir = Path.Combine(projectRoot, "XBX_Exports", "SkellAudits");
                Directory.CreateDirectory(outputDir);
                string output = Path.Combine(outputDir,
                    name + "_skell_resources.txt");
                File.WriteAllText(output, report.ToString(), Encoding.UTF8);
                Debug.Log("[XBX2Unity] Skell resource audit saved: " + output);
                EditorUtility.DisplayDialog("Skell Resource Audit",
                    "Read-only report saved:\n" + output, "OK");
            }
            catch (Exception e)
            {
                Debug.LogError("[XBX2Unity] Skell audit failed: " + e);
                EditorUtility.DisplayDialog("Skell Audit Failed", e.Message, "OK");
            }
        }

        [MenuItem("Assets/XBX2Unity/Audit Skell Textures Rig Animation", true)]
        private static bool Validate()
        {
            string p = AssetDatabase.GetAssetPath(Selection.activeObject);
            return !string.IsNullOrEmpty(p) &&
                p.EndsWith(".camdo", StringComparison.OrdinalIgnoreCase);
        }

        private static void AppendFileHeader(StringBuilder report, string path)
        {
            string filename = Path.GetFileName(path);
            if (!File.Exists(path))
            {
                report.AppendLine(filename + ": MISSING");
                return;
            }
            using (var input = File.OpenRead(path))
            {
                byte[] head = new byte[32];
                int read = input.Read(head, 0, head.Length);
                report.Append(filename).Append(" size=").Append(input.Length)
                    .Append(" first32=");
                for (int i = 0; i < read; i++)
                    report.Append(head[i].ToString("X2")).Append(' ');
                report.AppendLine();
            }
        }
        private static void AppendMtxtReport(StringBuilder report, string path)
        {
            if (!File.Exists(path)) return;
            FileInfo info = new FileInfo(path);
            if (info.Length > 64 * 1024 * 1024L)
            {
                report.AppendLine(Path.GetFileName(path) + " too large for bounded MTXT scan (" +
                    info.Length + " bytes).");
                return;
            }
            byte[] data = File.ReadAllBytes(path);
            int matches = 0;
            const int maxReportEntries = 150;
            for (int i = 108; i + 4 <= data.Length; i++)
            {
                if (data[i] != 0x4d || data[i+1] != 0x54 ||
                    data[i+2] != 0x58 || data[i+3] != 0x54) continue;
                int start = i - 108;
                uint width = BE32(data, start + 8);
                uint height = BE32(data, start + 12);
                uint format = BE32(data, start + 24);
                uint tileMode = BE32(data, start + 36);
                uint version = BE32(data, start + 104);
                if (width == 0 || height == 0 || width > 16384 || height > 16384 ||
                    (version != 10001 && version != 10002) ||
                    (format != 26 && (format < 49 || format > 53))) continue;
                matches++;
                if (matches <= maxReportEntries)
                    report.Append(Path.GetFileName(path))
                        .Append(" MTXT footer 0x").Append(start.ToString("X"))
                        .Append(" width=").Append(width).Append(" height=").Append(height)
                        .Append(" format=").Append(format).Append(" tileMode=")
                        .Append(tileMode).Append(" version=").Append(version)
                        .AppendLine();
            }
            report.AppendLine(Path.GetFileName(path) + " validated MTXT footers: " + matches);
        }
        private static uint BE32(byte[] b, int i) {
            return ((uint)b[i] << 24) | ((uint)b[i+1] << 16) |
                ((uint)b[i+2] << 8) | b[i+3];
        }
    }
}
#endif
