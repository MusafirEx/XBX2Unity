// Xenoblade Chronicles X Wii U animation-source discovery, Phase 1.
// The motion archive is in hkc.cpk (for example dl/dl019600.sar); a
// sibling *_rig.hkt beside CAMDO is the bind-pose skeleton, NOT an animation.
// This tool reads endian-specific SAR1 / 1RAS entries and copies their raw contents on request.
// It never fabricates keyframes or overwrites game archives.
#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace MusafirEx.XBX2Unity.Editor
{
    internal static class XBXSkellSarAnimationAudit
    {
        private const int MaxEntries = 4096;
        private const long MaxArchiveBytes = 512L * 1024 * 1024;
        private const int HeaderBytes = 160;
        private const int EntryBytes = 64;
        private static readonly byte[] HktMagic =
            { 0x57, 0xE0, 0xE0, 0x57, 0x10, 0xC0, 0xC0, 0x10 };

        internal sealed class Entry
        {
            internal int index;
            internal string name;
            internal uint nameHash;
            internal long offset;
            internal int size;
            internal string format;
        }

        internal sealed class Archive
        {
            internal string sourcePath;
            internal bool bigEndian;
            internal uint version;
            internal long dataOffset;
            internal string sourceName;
            internal readonly List<Entry> entries = new List<Entry>();
        }

        [MenuItem("Tools/XBX2Unity/Animation/Audit Skell SAR", false, 190)]
        [MenuItem("Assets/XBX2Unity/Audit Skell Animation SAR", false, 2106)]
        internal static void RunSelected()
        {
            try
            {
                string path = ChooseSource();
                if (string.IsNullOrEmpty(path)) return;
                Archive archive = Read(path);
                string folder = Path.Combine(ProjectRoot(), "XBX_Exports", "AnimationAudits");
                Directory.CreateDirectory(folder);
                string target = Path.Combine(folder, SafeFileName(
                    Path.GetFileNameWithoutExtension(path)) + "_sar_audit.txt");
                File.WriteAllText(target, FormatReport(archive), new UTF8Encoding(false));
                Debug.Log("[XBX2Unity] Skell animation SAR audit: " + target +
                    " (" + archive.entries.Count + " entries)");
                EditorUtility.RevealInFinder(target);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[XBX2Unity] Animation SAR audit: " + ex.Message);
                EditorUtility.DisplayDialog("Skell Animation SAR Audit",
                    ex.Message + "\n\nFor unsupported headers, inspect the first bytes of the actual .sar " +
                    "file before treating it as 1RAS. No game files were modified.", "OK");
            }
        }

        [MenuItem("Tools/XBX2Unity/Animation/Extract Raw Skell SAR Entries", false, 191)]
        [MenuItem("Assets/XBX2Unity/Extract Raw Skell Animation SAR", false, 2107)]
        internal static void ExtractSelected()
        {
            try
            {
                string path = ChooseSource();
                if (string.IsNullOrEmpty(path)) return;
                Archive archive = Read(path);
                string suggested = Path.Combine(ProjectRoot(), "XBX_Exports", "AnimationSources");
                Directory.CreateDirectory(suggested);
                string destination = EditorUtility.OpenFolderPanel(
                    "Extract original animation entries (not Unity .anim files)",
                    suggested, SafeFileName(Path.GetFileNameWithoutExtension(path)));
                if (string.IsNullOrEmpty(destination)) return;

                // Do not copy untrusted archive paths or overwrite existing data.
                int extracted = Extract(archive, destination);
                Debug.Log("[XBX2Unity] Extracted " + extracted + " SAR entries to " +
                    destination + "; native Havok clips require decoding before Unity playback.");
                EditorUtility.RevealInFinder(destination);
            }
            catch (Exception ex)
            {
                Debug.LogError("[XBX2Unity] Skell animation extraction failed: " + ex.Message);
                EditorUtility.DisplayDialog("Skell SAR Extraction Failed", ex.Message, "OK");
            }
        }

        [MenuItem("Assets/XBX2Unity/Audit Skell Animation SAR", true)]
        [MenuItem("Assets/XBX2Unity/Extract Raw Skell Animation SAR", true)]
        private static bool ValidateSelected()
        {
            string p = AssetDatabase.GetAssetPath(Selection.activeObject);
            return !string.IsNullOrEmpty(p) &&
                (p.EndsWith(".sar", StringComparison.OrdinalIgnoreCase) ||
                 p.EndsWith(".camdo", StringComparison.OrdinalIgnoreCase));
        }

        private static string ChooseSource()
        {
            string selected = AssetDatabase.GetAssetPath(Selection.activeObject);
            if (!string.IsNullOrEmpty(selected) &&
                selected.EndsWith(".sar", StringComparison.OrdinalIgnoreCase))
                return Path.GetFullPath(selected);

            // .camdo and *_rig.hkt aren't motion archives. Prompt for the
            // matching hkc.cpk/dl/*.sar instead of guessing motion from CEA.
            string initial = !string.IsNullOrEmpty(selected)
                ? Path.GetDirectoryName(Path.GetFullPath(selected))
                : ProjectRoot();
            return EditorUtility.OpenFilePanel("Select original XCX .sar (hkc.cpk)",
                initial, "sar");
        }

        private static string ProjectRoot()
        {
            return Directory.GetParent(Application.dataPath).FullName;
        }

        internal static Archive Read(string path)
        {
            if (string.IsNullOrEmpty(path) ||
                !path.EndsWith(".sar", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Choose a .sar from hkc.cpk, not a CAMDO or rig HKT.");
            FileInfo file = new FileInfo(path);
            if (!file.Exists) throw new FileNotFoundException("SAR archive not found.", path);
            if (file.Length < HeaderBytes || file.Length > MaxArchiveBytes)
                throw new InvalidDataException("SAR archive outside safe size limit: " + file.Length);
            byte[] data = File.ReadAllBytes(path);
            // The original Wii U uses "SAR1" big-endian, whereas later
            // Xenoblade archives can use "1RAS" little-endian.
            // Endianness follows the magic; do not guess by trying both.
            bool bigEndian = data[0] == (byte)'S' && data[1] == (byte)'A' &&
                data[2] == (byte)'R' && data[3] == (byte)'1';
            bool littleEndian = data[0] == (byte)'1' && data[1] == (byte)'R' &&
                data[2] == (byte)'A' && data[3] == (byte)'S';
            if (!bigEndian && !littleEndian)
                throw new InvalidDataException("Unknown SAR signature: " +
                    HexHeader(data) + ". Expected SAR1 (Wii U) or 1RAS.");

            Archive archive = Parse(data, bigEndian);
            archive.sourcePath = Path.GetFullPath(path);
            return archive;
        }

        private static Archive Parse(byte[] data, bool bigEndian)
        {
            long declared = U32(data, 4, bigEndian);
            uint version = U32(data, 8, bigEndian);
            uint entryCount = U32(data, 12, bigEndian);
            long table = U32(data, 16, bigEndian);
            long firstData = U32(data, 20, bigEndian);

            // Version 0x101 is present in the user's Wii U SAR1 header.
            // 0x102 uses 64-byte entries, 0x210 uses 96-byte entries.
            // Reject unknown versions rather than silently shifting names.
            int entryBytes, nameBytes;
            if (version == 0x101 || version == 0x102)
            {
                entryBytes = EntryBytes;
                nameBytes = 52;
            }
            else if (version == 0x210)
            {
                entryBytes = 96;
                nameBytes = 36;
            }
            else throw new InvalidDataException("unsupported SAR version 0x" +
                version.ToString("X") + " (magic " +
                (bigEndian ? "SAR1" : "1RAS") + ")");

            if (entryCount == 0 || entryCount > MaxEntries)
                throw new InvalidDataException("invalid entry count " + entryCount);
            if (table < HeaderBytes || table + (long)entryCount * entryBytes > data.Length)
                throw new InvalidDataException("entry table points outside archive");
            if (firstData != 0 && (firstData < HeaderBytes || firstData > data.Length))
                throw new InvalidDataException("invalid first-data offset " + firstData);
            if (declared != 0 && (declared < HeaderBytes || declared > data.Length))
                throw new InvalidDataException("declared file size 0x" +
                    declared.ToString("X") + " exceeds available archive 0x" +
                    data.Length.ToString("X") + " (truncated or wrong file)");

            var archive = new Archive
            {
                bigEndian = bigEndian, version = version, dataOffset = firstData,
                sourceName = AsciiName(data, 32, 128)
            };
            for (int i = 0; i < entryCount; i++)
            {
                int at = checked((int)(table + i * entryBytes));
                long offset = U32(data, at, bigEndian);
                long count = U32(data, at + 4, bigEndian);
                uint hash = U32(data, at + 8, bigEndian);
                string name = AsciiName(data, at + 12, nameBytes);
                if (count == 0) continue;
                if (count > int.MaxValue || offset < table + entryCount * entryBytes ||
                    offset > data.Length || count > data.Length - offset)
                    throw new InvalidDataException("entry #" + i + " outside archive bounds");
                // Keep source names as metadata, NEVER use raw paths for extraction.
                archive.entries.Add(new Entry
                {
                    index = i, name = name, nameHash = hash,
                    offset = offset, size = (int)count,
                    format = Identify(data, (int)offset, (int)count)
                });
            }
            if (archive.entries.Count == 0)
                throw new InvalidDataException("no nonempty entries after validation");
            return archive;
        }

        private static string Identify(byte[] bytes, int start, int length)
        {
            if (length >= 8)
            {
                bool hkt = true;
                for (int i = 0; i < HktMagic.Length; i++)
                    if (bytes[start+i] != HktMagic[i]) { hkt = false; break; }
                if (hkt) return HavokTypes(bytes, start, length);
            }
            if (length >= 4 && bytes[start] == (byte)'B' &&
                bytes[start+1] == (byte)'C' && bytes[start+2] == 0 && bytes[start+3] == 0)
                return "BC animation/skeleton container (not HKT)";
            return "Unknown raw data; header " + HexHeader(bytes, start, Math.Min(8, length));
        }

        private static string HavokTypes(byte[] data, int offset, int length)
        {
            // Havok packfile class names are plain ASCII; inspect conservatively
            // for codec names without assuming their keyframe layout.
            int bytes = Math.Min(length, 65536);
            string segment = Encoding.ASCII.GetString(data, offset, bytes);
            string[] known =
            {
                "hkaSplineCompressedAnimation",
                "hkaDeltaCompressedAnimation",
                "hkaInterleavedUncompressedAnimation",
                "hkaWaveletCompressedAnimation",
                "hkaAnimationBinding",
                "hkaAnimationContainer",
                "hkaSkeleton"
            };
            var found = new List<string>();
            foreach (string type in known)
                if (segment.IndexOf(type, StringComparison.Ordinal) >= 0)
                    found.Add(type);
            return found.Count == 0 ? "Havok HKT packfile (codec unknown)" :
                "Havok HKT; class hints: " + string.Join(", ", found.ToArray());
        }

        internal static int Extract(Archive archive, string destination)
        {
            if (archive == null || archive.entries.Count == 0)
                throw new InvalidDataException("No validated SAR entries.");
            if (string.IsNullOrEmpty(destination)) throw new ArgumentException("Missing output folder");
            Directory.CreateDirectory(destination);
            string outputRoot = Path.GetFullPath(destination);
            int count = 0;
            using (FileStream input = File.OpenRead(archive.sourcePath))
            {
                foreach (Entry entry in archive.entries)
                {
                    string name = SafeFileName(Path.GetFileName(entry.name.Replace('\\','/')));
                    if (string.IsNullOrEmpty(name)) name = "raw_entry";
                    string fileName = entry.index.ToString("D4") + "_" + name;
                    string output = Path.Combine(outputRoot, fileName);
                    // Don't overwrite previous user's exports.
                    if (File.Exists(output)) throw new IOException("Output exists: " + output);
                    var chunk = new byte[entry.size];
                    input.Position = entry.offset;
                    int read = 0;
                    while (read < chunk.Length)
                    {
                        int n = input.Read(chunk, read, chunk.Length - read);
                        if (n == 0) throw new EndOfStreamException("Truncated SAR entry " + entry.index);
                        read += n;
                    }
                    using (FileStream stream = new FileStream(output, FileMode.CreateNew))
                        stream.Write(chunk, 0, chunk.Length);
                    count++;
                }
            }
            return count;
        }

        private static string FormatReport(Archive a)
        {
            var s = new StringBuilder();
            s.AppendLine("XBX2Unity 0.10.0 - Skell Animation SAR Source Audit (READ ONLY)");
            s.AppendLine("Archive: " + a.sourcePath);
            s.AppendLine("Archive name: " + a.sourceName);
            s.AppendLine("Version: " + a.version + " | Endian: " +
                (a.bigEndian ? "Big" : "Little") + " | Data offset: 0x" +
                a.dataOffset.ToString("X"));
            s.AppendLine("Validated entries: " + a.entries.Count);
            s.AppendLine("NOTE: These are source assets, not decoded AnimationClip data.");
            s.AppendLine("NOTE: *_rig.hkt beside CAMDO is a skeleton, not this motion archive.");
            s.AppendLine("NOTE: Track names, duration, keyframes, binding and root motion require native HKT parsing.");
            s.AppendLine();
            foreach (Entry e in a.entries)
                s.Append(e.index.ToString("D4")).Append(" offset=0x")
                    .Append(e.offset.ToString("X")).Append(" bytes=").Append(e.size)
                    .Append(" hash=0x").Append(e.nameHash.ToString("X8"))
                    .Append(" name=").Append(e.name).Append(" | ").AppendLine(e.format);
            return s.ToString();
        }

        private static uint U32(byte[] bytes, int start, bool be)
        {
            if (start < 0 || start > bytes.Length - 4)
                throw new InvalidDataException("SAR integer outside archive bounds");
            return be
                ? ((uint)bytes[start]<<24 | (uint)bytes[start+1]<<16 |
                   (uint)bytes[start+2]<<8 | bytes[start+3])
                : ((uint)bytes[start] | (uint)bytes[start+1]<<8 |
                   (uint)bytes[start+2]<<16 | (uint)bytes[start+3]<<24);
        }

        private static string AsciiName(byte[] bytes, int start, int limit)
        {
            int end = start;
            while (end < start+limit && bytes[end] != 0)
            {
                if (bytes[end] < 32 || bytes[end] > 126)
                    throw new InvalidDataException("Invalid archive name byte at " + end);
                end++;
            }
            return Encoding.ASCII.GetString(bytes, start, end-start);
        }

        private static string SafeFileName(string name)
        {
            if (string.IsNullOrEmpty(name)) return "";
            var b = new StringBuilder();
            foreach (char c in name)
            {
                if (char.IsLetterOrDigit(c) || c == '.' || c == '_' || c == '-')
                    b.Append(c);
                else b.Append('_');
            }
            string result = b.ToString().Trim('.');
            return result.Length > 120 ? result.Substring(0, 120) : result;
        }

        private static string HexHeader(byte[] bytes) { return HexHeader(bytes,0,Math.Min(bytes.Length,16)); }
        private static string HexHeader(byte[] bytes, int start, int size)
        {
            var b = new StringBuilder();
            for (int i=0; i<size; i++)
            {
                if (i != 0) b.Append(' ');
                b.Append(bytes[start+i].ToString("X2"));
            }
            return b.ToString();
        }
    }
}
#endif
