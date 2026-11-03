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

        // Entries are grounded in validated Havok virtual fixups. These are
        // typed object STARTS, not inferred animation channels or keyframes.
        internal sealed class ObjectRef
        {
            internal int section;
            internal int offset;
            internal int absolute;
            internal string className;
            internal string headerHex;
        }
        internal sealed class PointerRef
        {
            internal int section;
            internal int source;
            internal int targetSection;
            internal int target;
            internal bool isGlobal;
        }

        // Verified scalar fields and pointer-backed binding array.
        // Spline channel/keyframe decoding is NOT performed by this probe.
        internal sealed class MotionProbe
        {
            internal string skeletonName;
            internal int trackCount;
            internal int floatTrackCount;
            internal int frameCount;
            internal int blockCount;
            internal int maxFramesPerBlock;
            internal int maskAndQuantizationBytes;
            internal float durationSeconds;
            internal float frameDurationSeconds;
            internal float blockDurationSeconds;
            internal float inverseBlockDuration;
            internal int[] trackToBone;
            internal int referenceFrameSamples;
            internal bool referenceFrameSamples16Bytes;
        }

        internal sealed class Result
        {
            internal string path;
            internal string error;
            internal string version;
            internal bool bigEndian;
            internal int pointerBytes;
            internal int sectionCount;
            internal int sectionStride;
            internal int sectionFieldsOffset;
            internal string rootClass = "unresolved";
            internal int localFixups;
            internal int globalFixups;
            internal int virtualFixups;
            internal int dataBytes;
            internal readonly Dictionary<string, int> classes =
                new Dictionary<string, int>(StringComparer.Ordinal);
            internal readonly List<string> warnings = new List<string>();
            internal readonly List<ObjectRef> objects = new List<ObjectRef>();
            internal readonly List<PointerRef> pointers = new List<PointerRef>();
            internal MotionProbe motion;
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
            sb.AppendLine("Phase 2B: object offsets are real virtual-fixup references; headers are raw bytes, NOT decoded Havok fields.");
            bool includeHeaders = results.Count == 1;
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
                sb.AppendLine("  Verified section layout: stride=" + r.sectionStride +
                    " bytes, fields=+0x" + r.sectionFieldsOffset.ToString("X"));
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
                sb.AppendLine("  Typed object references: " + r.objects.Count +
                    " | Validated pointer references: " + r.pointers.Count);
                if (r.motion != null)
                {
                    MotionProbe m = r.motion;
                    sb.AppendLine("  PHASE 2C MOTION METADATA (verified fields, not keyframes):");
                    sb.AppendLine("    Original skeleton name: " + m.skeletonName);
                    sb.AppendLine("    Duration seconds: " +
                        m.durationSeconds.ToString("R",
                            System.Globalization.CultureInfo.InvariantCulture));
                    sb.AppendLine("    Frames: " + m.frameCount +
                        " | Frame duration seconds: " +
                        m.frameDurationSeconds.ToString("R",
                            System.Globalization.CultureInfo.InvariantCulture) +
                        " | Approx FPS: " +
                        (1.0f / m.frameDurationSeconds).ToString("F4",
                            System.Globalization.CultureInfo.InvariantCulture));
                    sb.AppendLine("    Transform tracks: " + m.trackCount +
                        " | Float tracks: " + m.floatTrackCount);
                    sb.AppendLine("    Compressed blocks: " + m.blockCount +
                        " | Max frames/block: " + m.maxFramesPerBlock +
                        " | Mask/quantization bytes: " + m.maskAndQuantizationBytes);
                    sb.AppendLine("    Block duration: " +
                        m.blockDurationSeconds.ToString("R",
                            System.Globalization.CultureInfo.InvariantCulture) +
                        " | Inverse: " +
                        m.inverseBlockDuration.ToString("R",
                            System.Globalization.CultureInfo.InvariantCulture));
                    sb.AppendLine("    Binding track->bone index entries: " + m.trackToBone.Length);
                    if (m.referenceFrameSamples > 0)
                        sb.AppendLine("    Reference-frame samples: " + m.referenceFrameSamples +
                            " | 16-byte records fit: " + m.referenceFrameSamples16Bytes);
                    int displayed = includeHeaders ? m.trackToBone.Length :
                        Math.Min(8, m.trackToBone.Length);
                    for (int k = 0; k < displayed; k++)
                        sb.AppendLine("    Track " + k.ToString("D3") + " -> bone index " +
                            m.trackToBone[k]);
                    if (displayed < m.trackToBone.Length)
                        sb.AppendLine("    ... " + (m.trackToBone.Length - displayed) +
                            " additional mapping entries (select one HKT for complete list).");
                }
                foreach (ObjectRef obj in r.objects.OrderBy(x => x.section)
                    .ThenBy(x => x.offset).Take(32))
                {
                    sb.AppendLine("  Object " + obj.className +
                        " section=" + obj.section +
                        " rel=0x" + obj.offset.ToString("X") +
                        " abs=0x" + obj.absolute.ToString("X"));
                    if (includeHeaders)
                    {
                        sb.AppendLine("    Header bytes (raw, up to 96): " + obj.headerHex);
                        int linked = 0;
                        foreach (PointerRef ptr in r.pointers.Where(p =>
                            p.section == obj.section &&
                            p.source >= obj.offset &&
                            p.source < NextObjectOffset(r.objects, obj, r.dataBytes)))
                        {
                            if (linked < 32)
                                sb.AppendLine("    " + (ptr.isGlobal ? "GLOBAL" : "LOCAL") +
                                    " pointer src=0x" + ptr.source.ToString("X") +
                                    " -> section=" + ptr.targetSection +
                                    " rel=0x" + ptr.target.ToString("X"));
                            linked++;
                        }
                        if (linked > 32)
                            sb.AppendLine("    ... " + (linked - 32) +
                                " additional pointers omitted.");
                    }
                }
                if (r.objects.Count > 32)
                    sb.AppendLine("  ... more typed objects omitted.");
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

            // Havok packfiles have several section-header layouts. The Wii U
            // rig reader treats a section as 16-byte tag + eight uint32 words;
            // other Havok variants use 20-byte tag + seven uint32 words.
            // Choose a layout only if ALL section names and bounds validate.
            // Never silently skip a broken __types__ section.
            int headerStride;
            int fieldsOffset;
            var sections = ReadSections(data, bigEndian, (int)numberSections,
                out headerStride, out fieldsOffset);
            result.sectionStride = headerStride;
            result.sectionFieldsOffset = fieldsOffset;
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
                    result.objects.Add(new ObjectRef {
                        section = i, offset = (int)objectOffset,
                        absolute = sec.start + (int)objectOffset,
                        className = className
                    });
                    if (i == rootSection && objectOffset == rootOffset)
                        result.rootClass = className;
                }
            }
            // Pointer records can locate hkaAnimationBinding's internal
            // hkArray fields and its referenced animation object. They do not
            // prove element counts, data types, track mapping or clip duration.
            CollectPointers(data, sections, bigEndian, result);
            foreach (ObjectRef obj in result.objects)
            {
                Section sec = sections[obj.section];
                int available = Math.Max(0, sec.local - obj.offset);
                int length = Math.Min(96, available);
                obj.headerHex = HexBytes(data, obj.absolute, length);
            }
            try
            {
                result.motion = ReadMotionMetadata(data, sections, bigEndian, result);
            }
            catch (InvalidDataException ex)
            {
                result.warnings.Add("Phase 2C metadata not verified: " + ex.Message);
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

        // Phase 2C: structural hkaSplineCompressedAnimation/hkaAnimationBinding
        // metadata, validated against the pointer fixups and frame timing.
        // 32-bit Havok 2013 layout; do not use for 64-bit packfiles.
        private static MotionProbe ReadMotionMetadata(byte[] data, Section[] sections,
            bool bigEndian, Result result)
        {
            if (result.pointerBytes != 4) return null;
            ObjectRef anim = result.objects.FirstOrDefault(o =>
                o.className == "hkaSplineCompressedAnimation");
            ObjectRef binding = result.objects.FirstOrDefault(o =>
                o.className == "hkaAnimationBinding");
            if (anim == null || binding == null) return null;
            Section animSection = sections[anim.section];
            Section bindingSection = sections[binding.section];
            if (anim.offset + 0x44 > animSection.local ||
                binding.offset + 0x38 > bindingSection.local)
                throw new InvalidDataException("Animation or binding metadata truncated");

            int a = anim.absolute;
            int b = binding.absolute;
            int codec = CheckedInt(U32(data, a + 0x08, bigEndian), 32, "animation codec");
            if (codec != 3)
                throw new InvalidDataException("Unexpected spline codec ID " + codec);
            var motion = new MotionProbe
            {
                durationSeconds = F32(data, a + 0x0C, bigEndian),
                trackCount = CheckedInt(U32(data, a + 0x10, bigEndian), 4096, "transform tracks"),
                floatTrackCount = CheckedInt(U32(data, a + 0x14, bigEndian), 4096, "float tracks"),
                frameCount = CheckedInt(U32(data, a + 0x28, bigEndian), 1000000, "frames"),
                blockCount = CheckedInt(U32(data, a + 0x2C, bigEndian), 4096, "blocks"),
                maxFramesPerBlock = CheckedInt(U32(data, a + 0x30, bigEndian),
                    65536, "frames per block"),
                maskAndQuantizationBytes = CheckedInt(U32(data, a + 0x34, bigEndian),
                    MaxFileBytes, "mask/quantization bytes"),
                blockDurationSeconds = F32(data, a + 0x38, bigEndian),
                inverseBlockDuration = F32(data, a + 0x3C, bigEndian),
                frameDurationSeconds = F32(data, a + 0x40, bigEndian)
            };
            if (!FinitePositive(motion.durationSeconds, 86400f) ||
                !FinitePositive(motion.frameDurationSeconds, 10f) ||
                !FinitePositive(motion.blockDurationSeconds, 86400f) ||
                !FinitePositive(motion.inverseBlockDuration, 100000f) ||
                motion.trackCount < 1 || motion.frameCount < 2 ||
                motion.blockCount < 1 || motion.maxFramesPerBlock < 2)
                throw new InvalidDataException("Animation durations or track/frame counts not credible");
            if (Math.Abs((motion.frameCount - 1) * motion.frameDurationSeconds -
                motion.durationSeconds) > Math.Max(0.1f, motion.durationSeconds * 0.015f))
                throw new InvalidDataException("Duration conflicts with frame count/time step");
            if (Math.Abs(motion.blockDurationSeconds *
                motion.inverseBlockDuration - 1f) > 0.02f)
                throw new InvalidDataException("Compressed block duration/inverse mismatch");

            // A binding points to its original skeleton name (local +0x08),
            // the animation object (global +0x0C), and an hkArray<short>
            // trackToBoneIndices (local +0x10, element count at +0x14).
            PointerRef animPointer = GetPointer(result, binding, 0x0C, true);
            if (animPointer == null || animPointer.targetSection != anim.section ||
                animPointer.target != anim.offset)
                throw new InvalidDataException("Binding does not point to this animation");
            PointerRef skeletonPointer = GetPointer(result, binding, 0x08, false);
            PointerRef mappingPointer = GetPointer(result, binding, 0x10, false);
            if (skeletonPointer == null || mappingPointer == null)
                throw new InvalidDataException("Binding lacks skeleton name or track-map pointer");
            motion.skeletonName = SafeAsciiString(data, sections,
                skeletonPointer.targetSection, skeletonPointer.target, 128);
            int mappingCount = CheckedInt(U32(data, b + 0x14, bigEndian),
                4096, "binding indices");
            uint mappingCapacity = U32(data, b + 0x18, bigEndian);
            if (mappingCount != motion.trackCount ||
                (mappingCapacity & 0x3fffffffu) < mappingCount)
                throw new InvalidDataException("Track-map size/capacity disagrees with animation");
            Section mapSection = sections[mappingPointer.targetSection];
            if ((long)mappingPointer.target + (long)mappingCount * 2 > mapSection.local)
                throw new InvalidDataException("Track-to-bone array exceeds section data");
            int mapAt = mapSection.start + mappingPointer.target;
            motion.trackToBone = new int[mappingCount];
            for (int i = 0; i < mappingCount; i++)
            {
                short bone = unchecked((short)U16(data, mapAt + i * 2, bigEndian));
                if (bone < 0)
                    throw new InvalidDataException("Negative bone index at track " + i);
                motion.trackToBone[i] = bone;
            }

            // Optional reference frame: count at +0x38 and sample pointer +0x34
            // are supported by the reference-frame HKT object in the Wii U sample.
            ObjectRef reference = result.objects.FirstOrDefault(o =>
                o.className == "hkaDefaultAnimatedReferenceFrame");
            if (reference != null && reference.offset + 0x40 <=
                sections[reference.section].local)
            {
                PointerRef samples = GetPointer(result, reference, 0x34, false);
                int sampleCount = CheckedInt(U32(data, reference.absolute + 0x38,
                    bigEndian), 1000000, "reference frame samples");
                float refDuration = F32(data, reference.absolute + 0x30, bigEndian);
                if (samples != null && sampleCount > 0 &&
                    Math.Abs(refDuration - motion.durationSeconds) <
                        Math.Max(0.05f, motion.durationSeconds * 0.01f))
                {
                    motion.referenceFrameSamples = sampleCount;
                    Section sampleSection = sections[samples.targetSection];
                    motion.referenceFrameSamples16Bytes =
                        (long)samples.target + (long)sampleCount * 16 <=
                        sampleSection.local;
                }
            }
            return motion;
        }

        private static PointerRef GetPointer(Result result, ObjectRef owner, int field,
            bool isGlobal)
        {
            return result.pointers.FirstOrDefault(p =>
                p.section == owner.section &&
                p.source == owner.offset + field &&
                p.isGlobal == isGlobal);
        }

        private static int CheckedInt(uint value, int max, string description)
        {
            if (value > max)
                throw new InvalidDataException(description + " beyond bounded range: " + value);
            return (int)value;
        }

        private static ushort U16(byte[] data, int at, bool bigEndian)
        {
            if (at < 0 || (long)at + 2 > data.Length)
                throw new InvalidDataException("Bone mapping outside HKT file");
            return bigEndian ? (ushort)((data[at] << 8) | data[at + 1]) :
                (ushort)(data[at] | (data[at + 1] << 8));
        }

        private static float F32(byte[] data, int at, bool bigEndian)
        {
            uint bits = U32(data, at, bigEndian);
            return BitConverter.ToSingle(BitConverter.GetBytes(bits), 0);
        }

        private static bool FinitePositive(float f, float maximum)
        {
            return !float.IsNaN(f) && !float.IsInfinity(f) && f > 0 && f < maximum;
        }

        private static string SafeAsciiString(byte[] data, Section[] sections,
            int section, int offset, int maxLength)
        {
            if (section < 0 || section >= sections.Length ||
                offset < 0 || offset >= sections[section].local)
                throw new InvalidDataException("Skeleton name outside section");
            Section sec = sections[section];
            int absolute = sec.start + offset;
            int allowed = Math.Min(maxLength, sec.local - offset);
            int count = 0;
            while (count < allowed && data[absolute + count] != 0)
            {
                byte c = data[absolute + count];
                if (c < 32 || c > 126)
                    throw new InvalidDataException("Nonprintable original skeleton name");
                count++;
            }
            if (count < 1 || count >= allowed)
                throw new InvalidDataException("Missing terminated original skeleton name");
            return Encoding.ASCII.GetString(data, absolute, count);
        }

        private static int NextObjectOffset(List<ObjectRef> all, ObjectRef source,
            int defaultEnd)
        {
            int end = defaultEnd;
            foreach (ObjectRef other in all)
                if (other.section == source.section && other.offset > source.offset &&
                    other.offset < end)
                    end = other.offset;
            return end;
        }

        private static void CollectPointers(byte[] data, Section[] sections,
            bool bigEndian, Result result)
        {
            for (int i = 0; i < sections.Length; i++)
            {
                Section sec = sections[i];
                int localCount = CountFixups(data, sec, sec.local, sec.global, 8,
                    "local fixups");
                for (int j = 0; j < localCount; j++)
                {
                    int at = sec.start + sec.local + 8 * j;
                    uint source = U32(data, at, bigEndian);
                    uint target = U32(data, at + 4, bigEndian);
                    if (source == uint.MaxValue) continue; // alignment/sentinel
                    if ((long)source + 4 > sec.local || target >= sec.local)
                    {
                        result.warnings.Add("Invalid local pointer at 0x" + at.ToString("X"));
                        continue;
                    }
                    result.pointers.Add(new PointerRef {
                        section = i, source = (int)source, targetSection = i,
                        target = (int)target, isGlobal = false
                    });
                }
                int globalCount = CountFixups(data, sec, sec.global, sec.virtuals, 12,
                    "global fixups");
                for (int j = 0; j < globalCount; j++)
                {
                    int at = sec.start + sec.global + 12 * j;
                    uint source = U32(data, at, bigEndian);
                    uint targetSection = U32(data, at + 4, bigEndian);
                    uint target = U32(data, at + 8, bigEndian);
                    if (source == uint.MaxValue) continue;
                    if ((long)source + 4 > sec.local ||
                        targetSection >= sections.Length ||
                        target >= sections[(int)targetSection].local)
                    {
                        result.warnings.Add("Invalid global pointer at 0x" + at.ToString("X"));
                        continue;
                    }
                    result.pointers.Add(new PointerRef {
                        section = i, source = (int)source,
                        targetSection = (int)targetSection,
                        target = (int)target, isGlobal = true
                    });
                }
            }
        }

        private static string HexBytes(byte[] data, int start, int count)
        {
            if (start < 0 || count < 0 || (long)start + count > data.Length)
                throw new InvalidDataException("Object header snapshot outside HKT file");
            var sb = new StringBuilder(count * 3);
            for (int i = 0; i < count; i++)
            {
                if (i != 0) sb.Append(' ');
                sb.Append(data[start + i].ToString("X2"));
            }
            return sb.ToString();
        }

        // Three layouts supported for header metadata audits:
        // 48/16: 16-byte tag + 8 32-bit words (xc3_lib HKT reader);
        // 48/20: 20-byte tag + 7 32-bit words (standard 32-bit HKX);
        // 64/20: same fields plus extra header tail (later Havok builds).
        // Candidates are checked against section names, relative ordering
        // and the physical source file length before any pointer access.
        private static Section[] ReadSections(byte[] data, bool bigEndian, int count,
            out int stride, out int fieldsOffset)
        {
            var strides = new[] { 48, 48, 64 };
            var fieldStarts = new[] { 16, 20, 20 };
            var failures = new List<string>();
            for (int layout = 0; layout < strides.Length; layout++)
            {
                int width = strides[layout];
                int first = fieldStarts[layout];
                try
                {
                    if (0x40L + (long)count * width > data.Length)
                        throw new InvalidDataException("section directory truncated");
                    var parsed = new Section[count];
                    for (int i = 0; i < count; i++)
                    {
                        int at = 0x40 + i * width;
                        string name = Ascii(data, at, 16);
                        if (i < 3 && count >= 3)
                        {
                            string expected = i == 0 ? "__classnames__" :
                                i == 1 ? "__types__" : "__data__";
                            if (name != expected)
                                throw new InvalidDataException("expected " + expected +
                                    " at 0x" + at.ToString("X") + ", found " + name);
                        }
                        int off = at + first;
                        uint[] raw = new uint[7];
                        for (int k = 0; k < raw.Length; k++)
                            raw[k] = U32(data, off + 4*k, bigEndian);
                        if (raw.Any(v => v == 0xffffffffu))
                            throw new InvalidDataException("sentinel section field at " +
                                name + " @0x" + at.ToString("X") + " values=" +
                                FormatFields(raw));
                        if (raw.Any(v => v > data.Length))
                            throw new InvalidDataException("section offset exceeds file: " +
                                name + " @0x" + at.ToString("X") + " words=" +
                                FormatFields(raw));
                        int dataStart = CheckedOffset(raw[0], data.Length, "section data start");
                        int local = CheckedOffset(raw[1], data.Length, "local fixup offset");
                        int global = CheckedOffset(raw[2], data.Length, "global fixup offset");
                        int virt = CheckedOffset(raw[3], data.Length, "virtual fixup offset");
                        int exports = CheckedOffset(raw[4], data.Length, "exports offset");
                        int imports = CheckedOffset(raw[5], data.Length, "imports offset");
                        int finish = CheckedOffset(raw[6], data.Length, "section end offset");
                        if (local > global || global > virt || virt > exports ||
                            exports > imports || imports > finish ||
                            (long)dataStart + finish > data.Length)
                            throw new InvalidDataException("bad section bounds: " + name +
                                " @0x" + at.ToString("X") + " words=" + FormatFields(raw));
                        parsed[i] = new Section
                        {
                            name = name, start = dataStart, local = local,
                            global = global, virtuals = virt, exports = exports,
                            imports = imports, end = finish
                        };
                    }
                    stride = width;
                    fieldsOffset = first;
                    return parsed;
                }
                catch (InvalidDataException ex)
                {
                    failures.Add(width + "B/+0x" + first.ToString("X") + ": " +
                        ex.Message);
                }
            }
            throw new InvalidDataException("No verified HKT section-header layout. " +
                string.Join(" | ", failures.ToArray()));
        }

        private static string FormatFields(uint[] raw)
        {
            var fields = new string[raw.Length];
            for (int i = 0; i < raw.Length; i++)
                fields[i] = "0x" + raw[i].ToString("X8");
            return string.Join(",", fields);
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
