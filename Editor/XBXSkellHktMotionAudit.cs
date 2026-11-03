// Phase 2A: read-only Havok 2013 packfile object/fixup audit for Xenoblade X.
// An HKT animation packfile is NOT an AnimationClip. Compressed keys, binding
// mappings and root motion are intentionally not invented or modified.
#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace MusafirEx.XBX2Unity.Editor
{
    internal static class XBXSkellHktMotionAudit
    {
        private const int MaxFiles = 4096;
        private const int MaxFileBytes = 128 * 1024 * 1024;
        private const long MaxBatchBytes = 512L * 1024 * 1024;
        private const int MaxSections = 16;
        private const int MaxFixupsPerFile = 250000;
        private static readonly byte[] HktMagic =
            { 0x57, 0xE0, 0xE0, 0x57, 0x10, 0xC0, 0xC0, 0x10 };

        private sealed class Section
        {
            internal string name;
            internal int start;
            internal int local;
            internal int global;
            internal int virtuals;
            internal int exports;
            internal int imports;
            internal int end;
        }

        internal sealed class Result
        {
            internal string path;
            internal string error;
            internal string version;
            internal bool bigEndian;
            internal int pointerBytes;
            internal int sectionCount;
            internal string rootClass = "unresolved";
            internal int localFixups;
            internal int globalFixups;
            internal int virtualFixups;
            internal int dataBytes;
            internal readonly Dictionary<string, int> classes =
                new Dictionary<string, int>(StringComparer.Ordinal);
            internal readonly List<string> warnings = new List<string>();
            internal bool Valid { get { return string.IsNullOrEmpty(error); } }
            internal int Count(string className)
            {
                int n; return classes.TryGetValue(className, out n) ? n : 0;
            }
        }

        [MenuItem("Tools/XBX2Unity/Animation/Audit Extracted HKT Packfiles", false, 192)]
        [MenuItem("Assets/XBX2Unity/Audit Havok HKT Motion", false, 2108)]
        internal static void RunSelected()
        {
            try
            {
                string[] paths = ChooseSources();
                if (paths.Length == 0) return;
                if (paths.Length > MaxFiles)
                    throw new InvalidDataException("Too many HKT files (max " + MaxFiles + ").");

                string exportDirectory = Path.Combine(ProjectRoot(), "XBX_Exports", "AnimationAudits");
                Directory.CreateDirectory(exportDirectory);
                string sourceName = paths.Length == 1 ?
                    Path.GetFileNameWithoutExtension(paths[0]) :
                    new DirectoryInfo(Path.GetDirectoryName(paths[0])).Name;
                string target = Path.Combine(exportDirectory,
                    SafeName(sourceName) + "_hkt_motion_audit.txt");
                string report = AuditFiles(paths);
                File.WriteAllText(target, report, new UTF8Encoding(false));
                Debug.Log("[XBX2Unity] HKT packfile motion audit saved: " + target +
                    " (" + paths.Length + " files; read-only, no AnimationClips generated)");
                EditorUtility.RevealInFinder(target);
            }
            catch (Exception ex)
            {
                Debug.LogError("[XBX2Unity] HKT packfile audit failed: " + ex.Message);
                EditorUtility.DisplayDialog("XBX2Unity HKT Motion Audit", ex.Message, "OK");
            }
        }

        [MenuItem("Assets/XBX2Unity/Audit Havok HKT Motion", true)]
        private static bool CanAuditSelected()
        {
            string path = AssetDatabase.GetAssetPath(Selection.activeObject);
            return !string.IsNullOrEmpty(path) &&
                (AssetDatabase.IsValidFolder(path) ||
                 path.EndsWith(".hkt", StringComparison.OrdinalIgnoreCase));
        }

        private static string[] ChooseSources()
        {
            string path = AssetDatabase.GetAssetPath(Selection.activeObject);
            if (!string.IsNullOrEmpty(path) && AssetDatabase.IsValidFolder(path))
            {
                string dir = Path.Combine(ProjectRoot(), path);
                return Directory.GetFiles(dir, "*", SearchOption.AllDirectories)
                    .Where(f => f.EndsWith(".hkt", StringComparison.OrdinalIgnoreCase))
                    .OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToArray();
            }
            if (!string.IsNullOrEmpty(path) &&
                path.EndsWith(".hkt", StringComparison.OrdinalIgnoreCase))
                return new[] { Path.Combine(ProjectRoot(), path) };
            string chosen = EditorUtility.OpenFilePanel(
                "Select an extracted Skell motion HKT", ProjectRoot(), "hkt");
            return string.IsNullOrEmpty(chosen) ? new string[0] : new[] { chosen };
        }

        private static string ProjectRoot()
        {
            return Directory.GetParent(Application.dataPath).FullName;
        }

        internal static string AuditFiles(string[] paths)
        {
            if (paths == null || paths.Length == 0)
                throw new ArgumentException("No extracted HKT files selected.");
            if (paths.Length > MaxFiles)
                throw new InvalidDataException("Too many source HKT files.");
            long totalBytes = 0;
            foreach (string path in paths)
            {
                var info = new FileInfo(path);
                if (!info.Exists) throw new FileNotFoundException("HKT file missing", path);
                if (info.Length < 64 || info.Length > MaxFileBytes)
                    throw new InvalidDataException("HKT size out of safe range: " + path);
                totalBytes += info.Length;
                if (totalBytes > MaxBatchBytes)
                    throw new InvalidDataException("HKT batch exceeds 512 MiB safe limit.");
            }

            var results = new List<Result>(paths.Length);
            foreach (string path in paths)
            {
                try { results.Add(Read(path)); }
                catch (Exception ex)
                {
                    results.Add(new Result { path = path, error = ex.Message });
                }
            }

            var counts = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (Result r in results)
                foreach (var item in r.classes)
                {
                    int n; counts.TryGetValue(item.Key, out n);
                    counts[item.Key] = n + item.Value;
                }
            int success = results.Count(r => r.Valid);
            var sb = new StringBuilder();
            sb.AppendLine("XBX2Unity - Wii U Havok HKT Motion Packfile Audit (READ ONLY)");
            sb.AppendLine("Files: " + results.Count + " | Valid packfiles: " + success +
                " | Errors: " + (results.Count - success) +
                " | Source bytes: " + totalBytes);
            sb.AppendLine("NOTICE: Object class references are NOT decoded bone tracks.");
            sb.AppendLine("NOTICE: Duration, frame rate, track-to-bone binding, spline keys " +
                "and root motion are unknown until native animation decoding.");
            sb.AppendLine("NOTICE: Input HKT, Skell rigs, meshes, materials and .mat files are untouched.");
            sb.AppendLine();
            sb.AppendLine("Class references aggregated from validated Havok virtual fixups:");
            foreach (var item in counts.OrderByDescending(x => x.Value).ThenBy(x => x.Key))
                sb.AppendLine("  " + item.Key + ": " + item.Value);
            sb.AppendLine();
            sb.AppendLine("Individual packfiles:");
            for (int i = 0; i < results.Count; i++)
            {
                Result r = results[i];
                sb.AppendLine();
                sb.AppendLine("[" + i.ToString("D4") + "] " + Path.GetFileName(r.path));
                if (!r.Valid)
                {
                    sb.AppendLine("  ERROR: " + r.error);
                    continue;
                }
                sb.AppendLine("  Havok: " + r.version + ", " +
                    (r.bigEndian ? "Big" : "Little") + "-endian, pointer bytes=" +
                    r.pointerBytes + ", sections=" + r.sectionCount +
                    ", data bytes=" + r.dataBytes);
                sb.AppendLine("  Root object: " + r.rootClass);
                sb.AppendLine("  Fixups: local=" + r.localFixups +
                    ", global=" + r.globalFixups + ", virtual=" + r.virtualFixups);
                sb.AppendLine("  Motion class refs: spline=" +
                    r.Count("hkaSplineCompressedAnimation") +
                    ", binding=" + r.Count("hkaAnimationBinding") +
                    ", container=" + r.Count("hkaAnimationContainer") +
                    ", skeleton=" + r.Count("hkaSkeleton"));
                foreach (var item in r.classes.OrderByDescending(x => x.Value).ThenBy(x => x.Key)
                    .Take(12))
                    sb.AppendLine("  Type: " + item.Key + " x" + item.Value);
                foreach (string warning in r.warnings)
                    sb.AppendLine("  WARNING: " + warning);
            }
            return sb.ToString();
        }

        internal static Result Read(string path)
        {
            FileInfo fi = new FileInfo(path);
            if (fi.Length < 208 || fi.Length > MaxFileBytes)
                throw new InvalidDataException("HKT header outside supported length");
            byte[] data = File.ReadAllBytes(path);
            for (int i = 0; i < HktMagic.Length; i++)
                if (data[i] != HktMagic[i])
                    throw new InvalidDataException("Not a native Havok HKT packfile");

            // hkPackfileHeader 2013: layout rules at 0x10, section count 0x14,
            // root section/offset at 0x18/0x1c; section directory at 0x40.
            int layoutPointerBytes = data[0x10];
            bool bigEndian = data[0x11] == 0;
            uint numberSections = U32(data, 0x14, bigEndian);
            if (layoutPointerBytes != 4 && layoutPointerBytes != 8)
                throw new InvalidDataException("Unexpected HKT pointer size: " +
                    layoutPointerBytes);
            if (numberSections < 1 || numberSections > MaxSections)
                throw new InvalidDataException("Invalid HKT section count " +
                    numberSections + " (check layout byte order)");
            if (0x40L + numberSections * 48 > data.Length)
                throw new InvalidDataException("HKT section directory is truncated");

            var result = new Result
            {
                path = path, bigEndian = bigEndian,
                pointerBytes = layoutPointerBytes, sectionCount = (int)numberSections,
                version = Ascii(data, 0x28, 16)
            };
            if (string.IsNullOrEmpty(result.version) ||
                !result.version.Contains("2013"))
                result.warnings.Add("Unverified Havok version; metadata layout may differ.");
            uint rootSection = U32(data, 0x18, bigEndian);
            uint rootOffset = U32(data, 0x1c, bigEndian);
            if (rootSection >= numberSections)
                throw new InvalidDataException("Root section outside directory");

            var sections = new Section[(int)numberSections];
            for (int i = 0; i < sections.Length; i++)
            {
                int at = 0x40 + i * 48;
                string name = Ascii(data, at, 16);
                int start = CheckedOffset(U32(data, at + 16, bigEndian),
                    data.Length, "section data start");
                int local = CheckedOffset(U32(data, at + 20, bigEndian),
                    data.Length, "local fixup offset");
                int global = CheckedOffset(U32(data, at + 24, bigEndian),
                    data.Length, "global fixup offset");
                int virtuals = CheckedOffset(U32(data, at + 28, bigEndian),
                    data.Length, "virtual fixup offset");
                int exports = CheckedOffset(U32(data, at + 32, bigEndian),
                    data.Length, "exports offset");
                int imports = CheckedOffset(U32(data, at + 36, bigEndian),
                    data.Length, "imports offset");
                int end = CheckedOffset(U32(data, at + 40, bigEndian),
                    data.Length, "section end offset");
                if (local > global || global > virtuals || virtuals > exports ||
                    exports > imports || imports > end ||
                    (long)start + end > data.Length)
                    throw new InvalidDataException("Bad section bounds: " + name);
                sections[i] = new Section
                {
                    name = name, start = start, local = local, global = global,
                    virtuals = virtuals, exports = exports, imports = imports, end = end
                };
            }
            if (!sections.Any(x => x.name == "__classnames__"))
                result.warnings.Add("No __classnames__ section; virtual object names may be unresolved.");
            if (!sections.Any(x => x.name == "__data__"))
                result.warnings.Add("No __data__ section; this may not be a motion HKT.");
            var root = sections[(int)rootSection];
            if (rootOffset >= root.local)
                result.warnings.Add("Root offset is outside normal object-data span.");

            for (int i = 0; i < sections.Length; i++)
            {
                Section sec = sections[i];
                if (sec.name == "__data__")
                    result.dataBytes = sec.local;
                result.localFixups += CountFixups(data, sec, sec.local, sec.global, 8,
                    "local fixups");
                result.globalFixups += CountFixups(data, sec, sec.global, sec.virtuals, 12,
                    "global fixups");
                int count = CountFixups(data, sec, sec.virtuals, sec.exports, 12,
                    "virtual fixups");
                result.virtualFixups += count;
                for (int j = 0; j < count; j++)
                {
                    int at = sec.start + sec.virtuals + j * 12;
                    uint objectOffset = U32(data, at, bigEndian);
                    uint classSectionIndex = U32(data, at + 4, bigEndian);
                    uint classOffset = U32(data, at + 8, bigEndian);
                    if (objectOffset == 0xffffffff && classSectionIndex == 0xffffffff)
                        continue; // 0xff section table padding
                    if (objectOffset >= sec.local || classSectionIndex >= sections.Length)
                    {
                        result.warnings.Add("Invalid object/class fixup at 0x" + at.ToString("X"));
                        continue;
                    }
                    Section classSection = sections[(int)classSectionIndex];
                    if (classOffset >= classSection.local)
                    {
                        result.warnings.Add("Class string offset outside section at 0x" +
                            at.ToString("X"));
                        continue;
                    }
                    string className = Ascii(data, classSection.start + (int)classOffset,
                        Math.Min(128, classSection.local - (int)classOffset));
                    if (!ValidClassName(className))
                    {
                        result.warnings.Add("Invalid class string at 0x" + at.ToString("X"));
                        continue;
                    }
                    int prev; result.classes.TryGetValue(className, out prev);
                    result.classes[className] = prev + 1;
                    if (i == rootSection && objectOffset == rootOffset)
                        result.rootClass = className;
                }
            }
            if (result.virtualFixups == 0)
                result.warnings.Add("No virtual-fixup class references found; metadata incomplete.");
            if (result.Count("hkaSplineCompressedAnimation") == 0 &&
                result.Count("hkaInterleavedUncompressedAnimation") == 0 &&
                result.Count("hkaDeltaCompressedAnimation") == 0)
                result.warnings.Add("No supported animation class reference found; " +
                    "inspect HKT section layout manually.");
            return result;
        }

        private static int CountFixups(byte[] bytes, Section sec,
            int begin, int end, int stride, string label)
        {
            long length = (long)end - begin;
            if (length < 0) throw new InvalidDataException("Negative " + label);
            int n = (int)(length / stride);
            if (n > MaxFixupsPerFile)
                throw new InvalidDataException("Too many " + label);
            // Havok fixup tables may terminate with 0xff and alignment padding.
            if (length % stride != 0)
            {
                int trailing = (int)(length % stride);
                int at = sec.start + begin + n * stride;
                for (int i = 0; i < trailing; i++)
                    if (bytes[at + i] != 0xff && bytes[at + i] != 0)
                        throw new InvalidDataException("Unexpected " + label +
                            " table padding at 0x" + (at + i).ToString("X"));
            }
            return n;
        }

        private static int CheckedOffset(uint value, int max, string label)
        {
            if (value > max)
                throw new InvalidDataException("HKT " + label + " exceeds file: " + value);
            return (int)value;
        }

        private static uint U32(byte[] bytes, int offset, bool bigEndian)
        {
            if (offset < 0 || (long)offset + 4 > bytes.Length)
                throw new InvalidDataException("HKT unsigned read outside file");
            if (bigEndian)
                return ((uint)bytes[offset] << 24) | ((uint)bytes[offset + 1] << 16) |
                    ((uint)bytes[offset + 2] << 8) | bytes[offset + 3];
            return ((uint)bytes[offset + 3] << 24) | ((uint)bytes[offset + 2] << 16) |
                ((uint)bytes[offset + 1] << 8) | bytes[offset];
        }

        private static string Ascii(byte[] bytes, int offset, int maxBytes)
        {
            if (offset < 0 || offset >= bytes.Length) return "";
            int length = 0;
            while (length < maxBytes && (long)offset + length < bytes.Length &&
                bytes[offset + length] != 0)
                length++;
            return Encoding.ASCII.GetString(bytes, offset, length);
        }

        private static bool ValidClassName(string name)
        {
            if (string.IsNullOrEmpty(name) || name.Length > 120) return false;
            char first = name[0];
            if (!(first >= 'A' && first <= 'Z') &&
                !(first >= 'a' && first <= 'z')) return false;
            foreach (char c in name)
                if (!(c >= 'A' && c <= 'Z') && !(c >= 'a' && c <= 'z') &&
                    !(c >= '0' && c <= '9') && c != '_')
                    return false;
            return true;
        }

        private static string SafeName(string name)
        {
            var sb = new StringBuilder();
            foreach (char c in name ?? "")
                sb.Append(char.IsLetterOrDigit(c) || c == '-' || c == '_' ? c : '_');
            return sb.Length == 0 ? "animation" : sb.ToString();
        }
    }
}
#endif
