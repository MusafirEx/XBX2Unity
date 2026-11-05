// Non-destructive, explicitly invoked conversion of validated Havok Skell
// spline poses to a real Unity Generic AnimationClip (.anim) for a matching
// CAMDO asset and its sibling *_rig.hkt skeleton.
#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace MusafirEx.XBX2Unity.Editor
{
    internal static class XBXSkellHktClipExporter
    {
        [MenuItem("Tools/XBX2Unity/Animation/Convert Selected HKT to Unity AnimationClip", false, 194)]
        [MenuItem("Assets/XBX2Unity/Convert HKT to AnimationClip (Skell)", false, 2109)]
        internal static void RunSelected()
        {
            AnimationClip clip = null;
            try
            {
                string sourceAsset = AssetDatabase.GetAssetPath(Selection.activeObject);
                string source = !string.IsNullOrEmpty(sourceAsset) &&
                    sourceAsset.EndsWith(".hkt", StringComparison.OrdinalIgnoreCase)
                    ? Path.Combine(ProjectRoot(), sourceAsset)
                    : EditorUtility.OpenFilePanel("Select extracted Skell motion HKT",
                        Path.Combine(ProjectRoot(), "Assets"), "hkt");
                if (string.IsNullOrEmpty(source)) return;
                if (!File.Exists(source) ||
                    !source.EndsWith(".hkt", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Select a native extracted .hkt motion file.");

                string camdo = EditorUtility.OpenFilePanel(
                    "Select matching imported Skell .camdo (with sibling _rig.hkt)",
                    Path.Combine(ProjectRoot(), "Assets"), "camdo");
                if (string.IsNullOrEmpty(camdo)) return;
                string camdoAssetPath = ToAssetPath(camdo);
                string rig = Path.Combine(Path.GetDirectoryName(camdo),
                    Path.GetFileNameWithoutExtension(camdo) + "_rig.hkt");
                if (!File.Exists(rig))
                    throw new FileNotFoundException(
                        "Matching *_rig.hkt was not found next to CAMDO.", rig);

                var importer = AssetImporter.GetAtPath(camdoAssetPath)
                    as XBXCamdoNativeImporter;
                if (importer == null || !importer.importSkellHktHierarchy)
                    throw new InvalidDataException(
                        "CAMDO needs XBX2Unity native importer with Import Skell HKT Hierarchy enabled.");
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(camdoAssetPath);
                if (prefab == null || prefab.transform.Find("Skell_Rig") == null)
                    throw new InvalidDataException(
                        "Imported CAMDO does not have Skell_Rig. Reimport with Skell HKT hierarchy.");

                XBXSkellHktMotionAudit.MotionProbe motion =
                    XBXSkellHktMotionAudit.ExtractNativeSpline(source);
                XBXSkellHktRig.Skeleton skeleton = XBXSkellHktRig.Read(rig);
                string[] bonePaths = ValidateRig(motion, skeleton, prefab);
                XBXSkellHktSplineDecoder.Poses poses =
                    XBXSkellHktSplineDecoder.Decode(motion);
                if (poses.trackCount != bonePaths.Length)
                    throw new InvalidDataException("Decoded track count differs from validated rig map.");

                string targetPath = EditorUtility.SaveFilePanelInProject(
                    "Save native Skell AnimationClip",
                    Path.GetFileNameWithoutExtension(source) + "_native",
                    "anim",
                    "Choose a NEW .anim asset; the source HKT, prefab, and .mat assets are not modified.");
                if (string.IsNullOrEmpty(targetPath)) return;
                if (AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(targetPath) != null)
                    throw new IOException("Refusing to overwrite existing asset: " + targetPath);

                clip = new AnimationClip
                {
                    name = Path.GetFileNameWithoutExtension(targetPath),
                    frameRate = 1f / motion.frameDurationSeconds,
                    legacy = false
                };
                for (int t = 0; t < poses.trackCount; t++)
                {
                    string path = bonePaths[t];
                    var pos = new Vector3[poses.frameCount];
                    var rot = new Quaternion[poses.frameCount];
                    for (int f = 0; f < poses.frameCount; f++)
                    {
                        Vector3 p = poses.positions[t][f] * importer.modelScale;
                        Quaternion q = poses.rotations[t][f];
                        if (importer.flipX)
                        {
                            p.x = -p.x;
                            q = new Quaternion(q.x, -q.y, -q.z, q.w);
                        }
                        pos[f] = p;
                        rot[f] = q;
                    }
                    SetVector3(clip, path, "m_LocalPosition", pos, clip.frameRate);
                    SetQuaternion(clip, path, rot, clip.frameRate);
                    SetVector3(clip, path, "m_LocalScale", poses.scales[t],
                        clip.frameRate);
                }
                clip.EnsureQuaternionContinuity();
                // Only automatically loop an explicit idle animation whose
                // sampled transforms return to their initial state.
                bool isSeamlessIdle = IsSeamlessIdle(source, poses);
                if (isSeamlessIdle)
                {
                    var settings = AnimationUtility.GetAnimationClipSettings(clip);
                    settings.loopTime = true;
                    AnimationUtility.SetAnimationClipSettings(clip, settings);
                }
                AssetDatabase.CreateAsset(clip, targetPath);
                AssetDatabase.SaveAssets();
                EditorGUIUtility.PingObject(clip);
                Selection.activeObject = clip;
                Debug.Log("[XBX2Unity] Native HKT spline AnimationClip saved: " +
                    targetPath + "; " + poses.trackCount + " tracks x " +
                    poses.frameCount + " frames at " + clip.frameRate.ToString("F4") +
                    " FPS; looped idle=" + isSeamlessIdle +
                    "; constant Transform curve groups collapsed to two endpoint keys" +
                    "; dynamic position axes=" + poses.dynamicTranslations +
                    ", dynamic rotation tracks=" + poses.dynamicRotations +
                    ", dynamic scale axes=" + poses.dynamicScales +
                    ". Parent/root motion extraction is not separately implemented.");
                EditorUtility.DisplayDialog("XBX2Unity - Native HKT Animation",
                    "Created " + Path.GetFileName(targetPath) + " with " +
                    poses.trackCount + " mapped bones and " + poses.frameCount +
                    " sampled frames. Source HKT, CAMDO, rig and materials were not changed." +
                    "\n\nEnsure the CAMDO has Import Skell Bone Weights enabled " +
                    "to see a skinned mesh deform, then test the clip in Animator.",
                    "OK");
            }
            catch (Exception ex)
            {
                if (clip != null && !AssetDatabase.Contains(clip))
                    UnityEngine.Object.DestroyImmediate(clip);
                Debug.LogError("[XBX2Unity] HKT AnimationClip export failed: " + ex);
                EditorUtility.DisplayDialog("XBX2Unity HKT Animation", ex.Message, "OK");
            }
        }

        [MenuItem("Assets/XBX2Unity/Convert HKT to AnimationClip (Skell)", true)]
        private static bool CanExport()
        {
            string path = AssetDatabase.GetAssetPath(Selection.activeObject);
            return !string.IsNullOrEmpty(path) &&
                path.EndsWith(".hkt", StringComparison.OrdinalIgnoreCase);
        }

        private static string[] ValidateRig(XBXSkellHktMotionAudit.MotionProbe motion,
            XBXSkellHktRig.Skeleton skeleton, GameObject prefab)
        {
            if (motion.trackToBone == null || motion.annotationNames == null ||
                motion.trackToBone.Length != motion.trackCount ||
                motion.annotationNames.Length != motion.trackCount)
                throw new InvalidDataException("Missing fully validated 1:1 HKT bone index/name map.");
            if (skeleton.bones.Count < motion.trackCount)
                throw new InvalidDataException(
                    "Target skeleton has fewer bones than HKT transform tracks.");
            var visited = new HashSet<int>();
            var paths = new string[motion.trackCount];
            for (int t = 0; t < motion.trackCount; t++)
            {
                int bone = motion.trackToBone[t];
                if (bone < 0 || bone >= skeleton.bones.Count ||
                    !visited.Add(bone))
                    throw new InvalidDataException(
                        "Invalid or duplicate binding bone index for track " + t + ": " + bone);
                string expected = motion.annotationNames[t];
                string actual = skeleton.bones[bone].name;
                if (!string.Equals(expected, actual, StringComparison.Ordinal))
                    throw new InvalidDataException(
                        "Track " + t + " requires bone[" + bone + "] '" +
                        expected + "', but selected rig has '" + actual + "'.");
                paths[t] = PathForBone(skeleton, bone);
                if (prefab.transform.Find(paths[t]) == null)
                    throw new InvalidDataException(
                        "Imported prefab is missing animation path: " + paths[t]);
            }
            return paths;
        }

        private static string PathForBone(XBXSkellHktRig.Skeleton skeleton, int bone)
        {
            var segments = new List<string>();
            var ancestors = new HashSet<int>();
            int current = bone;
            while (current >= 0)
            {
                if (current >= skeleton.bones.Count || !ancestors.Add(current))
                    throw new InvalidDataException(
                        "Invalid/cyclic HKT parent bone chain at " + bone);
                var b = skeleton.bones[current];
                string safeName = string.IsNullOrEmpty(b.name) ?
                    "bone_" + current.ToString("D3") :
                    b.name.Replace('/', '_').Replace('\\', '_');
                segments.Insert(0, safeName);
                current = b.parent;
            }
            segments.Insert(0, "Skell_Rig");
            return string.Join("/", segments.ToArray());
        }

        private static void SetVector3(AnimationClip clip, string path,
            string property, Vector3[] frames, float fps)
        {
            var x = new float[frames.Length];
            var y = new float[frames.Length];
            var z = new float[frames.Length];
            for (int f = 0; f < frames.Length; f++)
            {
                x[f] = frames[f].x;
                y[f] = frames[f].y;
                z[f] = frames[f].z;
            }
            // Keep both boundary samples for constant vector groups; this
            // yields exactly the same transform over the clip duration while
            // avoiding 201 redundant samples for every unchanged bone.
            bool constant = Uniform(frames);
            clip.SetCurve(path, typeof(Transform), property + ".x",
                BuildCurve(x, fps, constant));
            clip.SetCurve(path, typeof(Transform), property + ".y",
                BuildCurve(y, fps, constant));
            clip.SetCurve(path, typeof(Transform), property + ".z",
                BuildCurve(z, fps, constant));
        }

        private static void SetQuaternion(AnimationClip clip,
            string path, Quaternion[] frames, float fps)
        {
            var x = new float[frames.Length];
            var y = new float[frames.Length];
            var z = new float[frames.Length];
            var w = new float[frames.Length];
            for (int f = 0; f < frames.Length; f++)
            {
                x[f] = frames[f].x;
                y[f] = frames[f].y;
                z[f] = frames[f].z;
                w[f] = frames[f].w;
            }
            // Collapse the ENTIRE quaternion group together (not individual
            // components), so Unity receives consistent quaternion timestamps.
            bool constant = Uniform(frames);
            clip.SetCurve(path, typeof(Transform), "m_LocalRotation.x",
                BuildCurve(x, fps, constant));
            clip.SetCurve(path, typeof(Transform), "m_LocalRotation.y",
                BuildCurve(y, fps, constant));
            clip.SetCurve(path, typeof(Transform), "m_LocalRotation.z",
                BuildCurve(z, fps, constant));
            clip.SetCurve(path, typeof(Transform), "m_LocalRotation.w",
                BuildCurve(w, fps, constant));
        }

        private static bool Uniform(Vector3[] values)
        {
            for (int i = 1; i < values.Length; i++)
                if (values[i].x != values[0].x ||
                    values[i].y != values[0].y ||
                    values[i].z != values[0].z)
                    return false;
            return true;
        }

        private static bool Uniform(Quaternion[] values)
        {
            for (int i = 1; i < values.Length; i++)
                if (values[i].x != values[0].x ||
                    values[i].y != values[0].y ||
                    values[i].z != values[0].z ||
                    values[i].w != values[0].w)
                    return false;
            return true;
        }

        private static bool IsSeamlessIdle(string source,
            XBXSkellHktSplineDecoder.Poses poses)
        {
            string filename = Path.GetFileNameWithoutExtension(source);
            if (filename.IndexOf("idle", StringComparison.OrdinalIgnoreCase) < 0)
                return false;
            int last = poses.frameCount - 1;
            for (int i = 0; i < poses.trackCount; i++)
            {
                if ((poses.positions[i][0] - poses.positions[i][last]).sqrMagnitude >
                    0.00000001f ||
                    (poses.scales[i][0] - poses.scales[i][last]).sqrMagnitude >
                    0.00000001f ||
                    Quaternion.Angle(poses.rotations[i][0],
                        poses.rotations[i][last]) > 0.1f)
                    return false;
            }
            return true;
        }

        private static AnimationCurve BuildCurve(float[] values, float fps,
            bool constant = false)
        {
            if (values == null || values.Length < 2)
                throw new InvalidDataException("Not enough sampled HKT frames");
            if (constant)
            {
                float end = (values.Length - 1) / fps;
                return new AnimationCurve(
                    new Keyframe(0f, values[0], 0f, 0f),
                    new Keyframe(end, values[0], 0f, 0f));
            }
            var keys = new Keyframe[values.Length];
            for (int i = 0; i < keys.Length; i++)
            {
                float tangentLeft = i == 0 ? (values[1] - values[0]) * fps :
                    (values[i] - values[i - 1]) * fps;
                float tangentRight = i == keys.Length - 1 ?
                    tangentLeft : (values[i + 1] - values[i]) * fps;
                keys[i] = new Keyframe(i / fps, values[i],
                    tangentLeft, tangentRight);
            }
            return new AnimationCurve(keys);
        }

        private static string ToAssetPath(string absolute)
        {
            string root = Path.GetFullPath(Path.Combine(ProjectRoot(), "Assets"))
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string full = Path.GetFullPath(absolute);
            if (!full.StartsWith(root + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Selected CAMDO must be inside Project Assets.");
            return "Assets" + full.Substring(root.Length).Replace('\\', '/');
        }

        private static string ProjectRoot()
        {
            return Directory.GetParent(Application.dataPath).FullName;
        }
    }
}
#endif
