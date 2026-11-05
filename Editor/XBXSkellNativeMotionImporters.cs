// XBX2Unity native Wii U HKT/SAR importer.
// Unity Animator always consumes AnimationClip objects. The .hkt/.sar files
// remain original source assets; clips are generated in-memory as imported
// Unity assets/sub-assets, NOT exported to separate .anim files.
#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.AssetImporters;
using UnityEngine;

namespace MusafirEx.XBX2Unity.Editor
{
    internal static class XBXSkellMotionImport
    {
        internal sealed class Target
        {
            internal GameObject prefab;
            internal XBXSkellHktRig.Skeleton skeleton;
            internal float modelScale;
            internal bool flipX;
            internal string assetPath;
        }

        internal static Target Resolve(AssetImportContext ctx,
            GameObject selectedCamdo, bool autoSearch, string skeletonName)
        {
            string path = selectedCamdo != null
                ? AssetDatabase.GetAssetPath(selectedCamdo) : null;
            if (string.IsNullOrEmpty(path) && autoSearch)
            {
                string stem = skeletonName ?? "";
                if (stem.EndsWith("_Bone", StringComparison.OrdinalIgnoreCase))
                    stem = stem.Substring(0, stem.Length - 5);
                string[] guids = AssetDatabase.FindAssets(stem);
                foreach (string guid in guids)
                {
                    string candidate = AssetDatabase.GUIDToAssetPath(guid);
                    if (candidate.EndsWith(".camdo", StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(Path.GetFileNameWithoutExtension(candidate), stem,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        path = candidate;
                        break;
                    }
                }
            }
            if (string.IsNullOrEmpty(path) ||
                !path.EndsWith(".camdo", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException(
                    "Matching Skell CAMDO not assigned/found for skeleton '" +
                    skeletonName + "'. Set Skell CAMDO in this asset's Import Settings.");

            XBXCamdoNativeImporter camdoImporter =
                AssetImporter.GetAtPath(path) as XBXCamdoNativeImporter;
            if (camdoImporter == null || !camdoImporter.importSkellHktHierarchy)
                throw new InvalidDataException(
                    "CAMDO must use XBX2Unity native importer with " +
                    "Import Skell HKT Hierarchy enabled: " + path);

            string rigAssetPath = Path.Combine(
                    Path.GetDirectoryName(path) ?? "",
                    Path.GetFileNameWithoutExtension(path) + "_rig.hkt")
                .Replace('\\', '/');
            if (!rigAssetPath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase) ||
                !File.Exists(rigAssetPath))
                throw new FileNotFoundException(
                    "CAMDO sibling *_rig.hkt is missing. Import the matching " +
                    "Havok rig beside the CAMDO.", rigAssetPath);

            // Source + artifact dependencies trigger regeneration if the
            // skeleton, CAMDO hierarchy or its importer flipX/scale changes.
            ctx.DependsOnSourceAsset(rigAssetPath);
            ctx.DependsOnArtifact(path);
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null || prefab.transform.Find("Skell_Rig") == null)
                throw new InvalidDataException(
                    "CAMDO imported prefab has no Skell_Rig. Reimport " + path);
            return new Target
            {
                prefab = prefab,
                skeleton = XBXSkellHktRig.Read(rigAssetPath),
                flipX = camdoImporter.flipX,
                modelScale = camdoImporter.modelScale,
                assetPath = path
            };
        }

        internal static AnimationClip Build(string name, byte[] hkt,
            Target target, bool loopIdle)
        {
            var source = XBXSkellHktMotionAudit.ExtractNativeSpline(hkt, name);
            var poses = XBXSkellHktSplineDecoder.Decode(source);
            return XBXSkellHktClipExporter.BuildClip(
                name, source, poses, target.skeleton,
                target.prefab, target.flipX, target.modelScale, loopIdle, true);
        }

        // Additional CAMDOs get separate imported AnimationClip variants.
        // A single Transform curve path cannot address two differently named
        // native Skell roots at once. Each variant uses its OWN target rig.
        internal static List<Target> ResolveVariants(AssetImportContext ctx,
            GameObject[] compatibleCamdos, string mainAssetPath)
        {
            var variants = new List<Target>();
            if (compatibleCamdos == null) return variants;
            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (!string.IsNullOrEmpty(mainAssetPath)) paths.Add(mainAssetPath);
            foreach (GameObject candidate in compatibleCamdos)
            {
                if (candidate == null) continue;
                string path = AssetDatabase.GetAssetPath(candidate);
                if (string.IsNullOrEmpty(path) || !paths.Add(path)) continue;
                try
                {
                    variants.Add(Resolve(ctx, candidate, false, ""));
                }
                catch (Exception e)
                {
                    ctx.LogImportWarning("[XBX2Unity] Compatible target " +
                        path + " skipped: " + e.Message);
                }
            }
            return variants;
        }

        internal static string TargetGuid(Target target)
        {
            string guid = AssetDatabase.AssetPathToGUID(target.assetPath);
            if (string.IsNullOrEmpty(guid) || guid.Length != 32)
                throw new InvalidDataException(
                    "Cannot determine stable Unity GUID for target CAMDO " + target.assetPath);
            return guid;
        }

        internal static string VariantName(string original, Target target)
        {
            return original + "__" +
                SafeName(Path.GetFileNameWithoutExtension(target.assetPath));
        }

        internal static AnimationClip BuildForTarget(string name,
            XBXSkellHktMotionAudit.MotionProbe probe,
            XBXSkellHktSplineDecoder.Poses poses,
            Target target, bool loopIdle)
        {
            return XBXSkellHktClipExporter.BuildClip(
                name, probe, poses, target.skeleton,
                target.prefab, target.flipX, target.modelScale, loopIdle, true);
        }

        internal static TextAsset Manifest(string name, string message)
        {
            var result = new TextAsset(message) { name = name };
            return result;
        }

        internal static string SafeName(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return "unknown";
            var sb = new StringBuilder();
            foreach (char ch in raw)
                sb.Append(char.IsLetterOrDigit(ch) || ch == '_' || ch == '-' ?
                    ch : '_');
            string name = sb.ToString().Trim('_');
            if (name.Length > 100) name = name.Substring(0, 100);
            return string.IsNullOrEmpty(name) ? "unknown" : name;
        }

        internal static bool IsRig(string path)
        {
            return Path.GetFileNameWithoutExtension(path)
                .EndsWith("_rig", StringComparison.OrdinalIgnoreCase);
        }
    }

    [ScriptedImporter(1, "hkt")]
    public sealed class XBXSkellHktAssetImporter : ScriptedImporter
    {
        [Tooltip("Matching imported Skell CAMDO GameObject. If unset, the importer " +
                 "searches the project for the CAMDO named by the HKT skeleton.")]
        public GameObject skellCamdo;
        public bool autoFindSkellCamdo = true;
        [Tooltip("Additional compatible Skell CAMDO prefabs. The imported HKT " +
                 "will expose a separate AnimationClip child for each target; " +
                 "only the validated dlXXXXXX_Bone root alias may differ.")]
        public GameObject[] compatibleSkellCamdos = new GameObject[0];
        [Tooltip("Loop only idle clips whose first and last pose match.")]
        public bool loopSeamlessIdle = true;

        public override void OnImportAsset(AssetImportContext ctx)
        {
            string name = Path.GetFileNameWithoutExtension(ctx.assetPath);
            if (XBXSkellMotionImport.IsRig(ctx.assetPath))
            {
                var rigInfo = XBXSkellMotionImport.Manifest(name,
                    "XBX2Unity: This *_rig.hkt is a Skell skeleton, not an " +
                    "AnimationClip. Keep it beside its matching .camdo.\n" +
                    "File: " + ctx.assetPath);
                ctx.AddObjectToAsset("main", rigInfo);
                ctx.SetMainObject(rigInfo);
                return;
            }
            try
            {
                byte[] hkt = File.ReadAllBytes(ctx.assetPath);
                var probe = XBXSkellHktMotionAudit.ExtractNativeSpline(hkt, name);
                XBXSkellMotionImport.Target target =
                    XBXSkellMotionImport.Resolve(ctx, skellCamdo,
                        autoFindSkellCamdo, probe.skeletonName);
                var poses = XBXSkellHktSplineDecoder.Decode(probe);
                AnimationClip clip = XBXSkellMotionImport.BuildForTarget(
                    name, probe, poses, target, loopSeamlessIdle);
                ctx.AddObjectToAsset("main", clip);
                ctx.SetMainObject(clip);

                // Source clip remains main asset; compatible CAMDO variants
                // have stable sub-asset IDs and target-specific root paths.
                foreach (XBXSkellMotionImport.Target alternate in
                    XBXSkellMotionImport.ResolveVariants(
                        ctx, compatibleSkellCamdos, target.assetPath))
                {
                    try
                    {
                        AnimationClip variant = XBXSkellMotionImport.BuildForTarget(
                            XBXSkellMotionImport.VariantName(name, alternate),
                            probe, poses, alternate, loopSeamlessIdle);
                        ctx.AddObjectToAsset("compatible_" +
                            XBXSkellMotionImport.TargetGuid(alternate), variant);
                    }
                    catch (Exception retargetError)
                    {
                        ctx.LogImportWarning("[XBX2Unity] HKT compatible clip " +
                            alternate.assetPath + " skipped: " + retargetError.Message);
                    }
                }
            }
            catch (Exception ex)
            {
                string problem = "Native HKT animation not imported as clip: " +
                    ctx.assetPath + "\n" + ex.Message +
                    "\nAssign a matching Skell CAMDO in importer settings. " +
                    "Only verified Havok 2013 single-block THREECOMP40 " +
                    "motions are currently supported. Original HKT unchanged.";
                ctx.LogImportWarning(problem);
                TextAsset diagnostic = XBXSkellMotionImport.Manifest(name, problem);
                ctx.AddObjectToAsset("main", diagnostic);
                ctx.SetMainObject(diagnostic);
            }
        }
    }

    [ScriptedImporter(1, "sar")]
    public sealed class XBXSkellSarAssetImporter : ScriptedImporter
    {
        [Tooltip("Matching Skell CAMDO GameObject used to bind the clips " +
                 "to real Skell_Rig paths. Optional if auto-find succeeds.")]
        public GameObject skellCamdo;
        public bool autoFindSkellCamdo = true;
        [Tooltip("Additional compatible Skell CAMDO prefabs. This SAR creates " +
                 "separate AnimationClip sub-assets bound to each Skell root. " +
                 "Only a different dlXXXXXX_Bone root is permitted.")]
        public GameObject[] compatibleSkellCamdos = new GameObject[0];
        [Tooltip("Only loop idle animations with matching end/start pose.")]
        public bool loopSeamlessIdle = true;
        [Tooltip("Optional case-insensitive entry name filter. Empty = all.")]
        public string nameContains = "";
        [Tooltip("Maximum successful AnimationClip sub-assets. 0 = all supported entries.")]
        [Min(0)] public int maxClips = 0;

        public override void OnImportAsset(AssetImportContext ctx)
        {
            string archiveName = Path.GetFileNameWithoutExtension(ctx.assetPath);
            var summary = new StringBuilder();
            summary.AppendLine("XBX2Unity - NATIVE SAR ANIMATION CONTAINER");
            summary.AppendLine("Source: " + ctx.assetPath);
            summary.AppendLine("The original .sar is not extracted or modified.");
            int imported = 0, importedCompatible = 0, skipped = 0;
            var issues = new List<string>();
            try
            {
                XBXSkellSarAnimationAudit.Archive archive =
                    XBXSkellSarAnimationAudit.Read(ctx.assetPath);
                summary.AppendLine("Validated SAR entries: " + archive.entries.Count);
                // The source archive name is a Skell id like dl099100.
                // Individual HKT annotations still undergo strict rig
                // index/name verification before becoming Unity clips.
                XBXSkellMotionImport.Target target =
                    XBXSkellMotionImport.Resolve(ctx, skellCamdo,
                        autoFindSkellCamdo, archiveName + "_Bone");
                List<XBXSkellMotionImport.Target> compatible =
                    XBXSkellMotionImport.ResolveVariants(
                        ctx, compatibleSkellCamdos, target.assetPath);
                summary.AppendLine("Additional compatible CAMDO targets: " +
                    compatible.Count);

                // All records already passed archive-wide bounds validation.
                byte[] sar = File.ReadAllBytes(ctx.assetPath);
                foreach (XBXSkellSarAnimationAudit.Entry entry in archive.entries)
                {
                    string entryLabel = Path.GetFileNameWithoutExtension(
                        entry.name.Replace('\\', '/'));
                    if (!string.IsNullOrEmpty(nameContains) &&
                        entry.name.IndexOf(nameContains,
                            StringComparison.OrdinalIgnoreCase) < 0)
                    {
                        skipped++;
                        continue;
                    }
                    if (maxClips > 0 && imported >= maxClips)
                    {
                        skipped++;
                        continue;
                    }
                    if (!entry.format.StartsWith("Havok HKT",
                            StringComparison.Ordinal))
                    {
                        skipped++;
                        issues.Add("entry " + entry.index + ": not Havok HKT");
                        continue;
                    }
                    try
                    {
                        byte[] hkt = new byte[entry.size];
                        Buffer.BlockCopy(sar, (int)entry.offset, hkt, 0, entry.size);
                        string uniqueName = entry.index.ToString("D4") + "_" +
                            XBXSkellMotionImport.SafeName(entryLabel);
                        var probe = XBXSkellHktMotionAudit.ExtractNativeSpline(
                            hkt, uniqueName);
                        var poses = XBXSkellHktSplineDecoder.Decode(probe);
                        AnimationClip clip = XBXSkellMotionImport.BuildForTarget(
                            uniqueName, probe, poses, target, loopSeamlessIdle);
                        // Original stable IDs remain UNCHANGED so existing
                        // Animator references to source clips survive upgrade.
                        ctx.AddObjectToAsset("motion_" + entry.index.ToString("D4"), clip);
                        imported++;

                        // Reuse decoded spline poses, no second HKT parse or
                        // decompression for each compatible target.
                        foreach (XBXSkellMotionImport.Target alternate in compatible)
                        {
                            try
                            {
                                AnimationClip variant =
                                    XBXSkellMotionImport.BuildForTarget(
                                        XBXSkellMotionImport.VariantName(
                                            uniqueName, alternate),
                                        probe, poses, alternate, loopSeamlessIdle);
                                ctx.AddObjectToAsset(
                                    "motion_" + entry.index.ToString("D4") +
                                    "__" + XBXSkellMotionImport.TargetGuid(alternate),
                                    variant);
                                importedCompatible++;
                            }
                            catch (Exception retargetError)
                            {
                                issues.Add("entry " + entry.index +
                                    " compatible " + alternate.assetPath +
                                    ": " + retargetError.Message);
                            }
                        }
                    }
                    catch (Exception entryError)
                    {
                        skipped++;
                        issues.Add("entry " + entry.index + " [" + entry.name +
                            "]: " + entryError.Message);
                    }
                }
                summary.AppendLine("Imported source AnimationClip sub-assets: " + imported);
                summary.AppendLine("Imported compatible Skell AnimationClip variants: " +
                    importedCompatible);
                summary.AppendLine("Total AnimationClip sub-assets: " +
                    (imported + importedCompatible));
                summary.AppendLine("Filtered / skipped / unsupported original entries: " + skipped);
                summary.AppendLine("Matching rig: " + target.assetPath);
                if (issues.Count > 0)
                    ctx.LogImportWarning("SAR " + archiveName + ": " + imported +
                        " clips imported; " + issues.Count +
                        " entries unsupported. Expand SAR manifest for details.");
            }
            catch (Exception ex)
            {
                summary.AppendLine("IMPORT ISSUE: " + ex.Message);
                ctx.LogImportWarning("[XBX2Unity] SAR animations unavailable: " +
                    ex.Message + ". Assign matching CAMDO in importer settings.");
            }

            foreach (string issue in issues)
                summary.AppendLine("SKIPPED: " + issue);
            TextAsset manifest = XBXSkellMotionImport.Manifest(
                archiveName + "_Animations", summary.ToString());
            ctx.AddObjectToAsset("main", manifest);
            ctx.SetMainObject(manifest);
        }
    }
}
#endif
