// Read-only CAMDO header audit. Does not attempt to reconstruct model data.
// Use this when the CAMDO parser reports an absent Models/VertexData pointer.
#if UNITY_EDITOR
using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace MusafirEx.XBX2Unity.Editor
{
    internal static class XBXCamdoHeaderAudit
    {
        [MenuItem("Assets/XBX2Unity/Audit CAMDO Header", false, 2103)]
        [MenuItem("Tools/XBX2Unity/Audit Selected CAMDO Header", false, 150)]
        private static void Run()
        {
            string path = AssetDatabase.GetAssetPath(Selection.activeObject);
            if (string.IsNullOrEmpty(path) ||
                !path.EndsWith(".camdo", StringComparison.OrdinalIgnoreCase))
            {
                EditorUtility.DisplayDialog("CAMDO Header Audit",
                    "Select a .camdo file in the Unity Project window first.", "OK");
                return;
            }

            try
            {
                string root = Directory.GetParent(Application.dataPath).FullName;
                string fullPath = Path.GetFullPath(Path.Combine(root, path));
                if (!File.Exists(fullPath))
                    throw new FileNotFoundException("The selected CAMDO file is missing.", fullPath);

                long totalSize;
                byte[] header = new byte[128];
                int read = 0;
                using (var stream = File.OpenRead(fullPath))
                {
                    totalSize = stream.Length;
                    while (read < header.Length)
                    {
                        int n = stream.Read(header, read, header.Length - read);
                        if (n == 0) break;
                        read += n;
                    }
                }

                var report = new StringBuilder();
                report.AppendLine("XBX2Unity Native CAMDO Header Audit v0.5.2");
                report.AppendLine("Source: " + path);
                report.AppendLine("Size: " + totalSize.ToString("N0") + " bytes");
                report.AppendLine("Header bytes read: " + read);
                string companion = Path.ChangeExtension(fullPath, ".casmt");
                report.AppendLine("Adjacent CASMT: " + (File.Exists(companion)
                    ? new FileInfo(companion).Length.ToString("N0") + " bytes"
                    : "NOT FOUND"));
                report.AppendLine();

                for (int row = 0; row < read; row += 16)
                {
                    report.Append("0x").Append(row.ToString("X4")).Append("  ");
                    for (int i = 0; i < 16; i++)
                    {
                        if (row + i < read) report.Append(header[row + i].ToString("X2"));
                        else report.Append("  ");
                        report.Append(' ');
                    }
                    report.AppendLine();
                }

                report.AppendLine();
                if (read >= 8)
                {
                    report.AppendLine("Magic: " + Encoding.ASCII.GetString(header, 0, 4));
                    report.AppendLine("Version BE (0x04): " + BigEndian32(header, 4));
                    report.AppendLine("Version LE (0x04): " + BitConverter.ToUInt32(header, 4) +
                        " (Windows only; this is the *wrong* byte order for Wii U CAMDO)");
                }

                report.AppendLine("Root metadata offsets (big-endian, absolute file addresses):");
                var names = new[] { "Models", "Materials", "MaterialAnimations",
                    "VertexData", "Shaders", "PackedTextures", "Unknown", "Streaming" };
                for (int i = 0; i < names.Length; i++)
                {
                    int at = 0x08 + 4 * i;
                    if (read < at + 4) break;
                    uint value = BigEndian32(header, at);
                    string interpreted = value == 0 ? "NULL" :
                        value >= totalSize ? "OUT OF FILE" : "IN FILE";
                    report.Append("  ").Append(names[i]).Append(" [0x")
                        .Append(at.ToString("X2")).Append("] = 0x")
                        .Append(value.ToString("X8")).Append(" (")
                        .Append(value).Append(", ").Append(interpreted)
                        .AppendLine(")");
                }

                report.AppendLine();
                report.AppendLine("Reference: ScanMountGoat/xc3_lib, xc3_lib/src/mxmd/legacy.rs");
                report.AppendLine("No mesh, texture, or model data was modified.");
                string reportFolder = Path.Combine(root, "XBX_Exports", "CAMDO_Audits");
                Directory.CreateDirectory(reportFolder);
                string safeName = path.Replace("Assets/", "").Replace('/', '_').Replace('\\', '_');
                string auditPath = Path.Combine(reportFolder, safeName + ".header.txt");
                File.WriteAllText(auditPath, report.ToString(), Encoding.UTF8);
                Debug.Log("[XBX2Unity] CAMDO header audit saved:\n" + auditPath +
                    "\n\n" + report);
                EditorUtility.DisplayDialog("CAMDO Header Audit",
                    "Read-only header report saved:\n" + auditPath +
                    "\n\nCopy the report to share the pointer values.", "OK");
            }
            catch (Exception e)
            {
                Debug.LogError("[XBX2Unity] CAMDO header audit failed: " + e);
                EditorUtility.DisplayDialog("CAMDO Header Audit Failed", e.Message, "OK");
            }
        }

        [MenuItem("Assets/XBX2Unity/Audit CAMDO Header", true)]
        private static bool Validate()
        {
            string p = AssetDatabase.GetAssetPath(Selection.activeObject);
            return !string.IsNullOrEmpty(p) && p.EndsWith(".camdo", StringComparison.OrdinalIgnoreCase);
        }

        private static uint BigEndian32(byte[] b, int at)
        {
            return ((uint)b[at] << 24) | ((uint)b[at + 1] << 16) |
                   ((uint)b[at + 2] << 8) | b[at + 3];
        }
    }
}
#endif
