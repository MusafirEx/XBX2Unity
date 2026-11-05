// XBX2Unity AnimationClip-to-CAMDO hierarchy and skinning diagnostics.
// Read-only except explicit transient scene preview / asset reimport actions.
#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace MusafirEx.XBX2Unity.Editor
{
    internal sealed class XBXSkellAnimationBindingDiagnostics : EditorWindow
    {
        private GameObject modelRoot;
        private AnimationClip motion;
        private string report =
            "Select the imported CAMDO root GameObject (scene instance or prefab asset) " +
            "and the exported Skell .anim file, then click Analyze.";
        private Vector2 scroll;
        private bool ownsPreview;

        [MenuItem("Tools/XBX2Unity/Animation/Diagnose Skell Clip / Hierarchy", false, 195)]
        internal static void Open()
        {
            var window = GetWindow<XBXSkellAnimationBindingDiagnostics>(
                "Skell Animation Diagnostic");
            window.minSize = new Vector2(590f, 370f);
            window.AutoSelect();
            window.Show();
        }

        private void AutoSelect()
        {
            GameObject selected = Selection.activeGameObject;
            if (selected != null)
                modelRoot = FindModelRoot(selected);
            if (Selection.activeObject is AnimationClip)
                motion = (AnimationClip)Selection.activeObject;
        }

        private static GameObject FindModelRoot(GameObject selected)
        {
            Transform current = selected.transform;
            while (current.parent != null)
            {
                if (current.Find("Skell_Rig") != null)
                    break;
                current = current.parent;
            }
            return current.gameObject;
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Native HKT Animation Binding", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "The clip contains real bone curves. Its paths start with " +
                "'Skell_Rig/'. Attach Animator to the CAMDO MODEL ROOT, " +
                "not the Skell_Rig child. For visible mesh deformation, " +
                "the CAMDO needs valid SkinnedMeshRenderer bones.",
                MessageType.Info);
            modelRoot = (GameObject)EditorGUILayout.ObjectField(
                "Imported CAMDO Root", modelRoot, typeof(GameObject), true);
            motion = (AnimationClip)EditorGUILayout.ObjectField(
                "Exported .anim Clip", motion, typeof(AnimationClip), false);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Analyze paths, Animator and renderers", GUILayout.Height(28f)))
                    Analyze();
                using (new EditorGUI.DisabledScope(motion == null ||
                    string.IsNullOrEmpty(AssetDatabase.GetAssetPath(motion))))
                {
                    if (GUILayout.Button("Refresh clip import", GUILayout.Height(28f)))
                        RefreshClip();
                }
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(modelRoot == null ||
                    motion == null || !modelRoot.scene.IsValid()))
                {
                    if (GUILayout.Button("Preview bone pose at 50%", GUILayout.Height(26f)))
                        Preview();
                }
                using (new EditorGUI.DisabledScope(!ownsPreview))
                {
                    if (GUILayout.Button("Stop temporary preview", GUILayout.Height(26f)))
                        StopPreview();
                }
            }

            scroll = EditorGUILayout.BeginScrollView(scroll);
            EditorGUILayout.TextArea(report, GUILayout.ExpandHeight(true));
            EditorGUILayout.EndScrollView();
        }

        private void Analyze()
        {
            try
            {
                report = BuildReport(modelRoot, motion);
                Debug.Log("[XBX2Unity] Skell Animation Binding Diagnostic\n" + report);
            }
            catch (Exception ex)
            {
                report = "DIAGNOSTIC ERROR: " + ex.Message;
                Debug.LogError("[XBX2Unity] Skell binding diagnostic error: " + ex);
            }
        }

        internal static string BuildReport(GameObject root, AnimationClip clip)
        {
            if (root == null || clip == null)
                return "Assign BOTH an imported CAMDO model root and an AnimationClip.";

            var sb = new StringBuilder();
            sb.AppendLine("XBX2Unity - SKELL ANIMATION HIERARCHY / SKIN DIAGNOSTIC");
            sb.AppendLine("Model root: " + root.name +
                (root.scene.IsValid() ? " (scene GameObject)" : " (project asset)"));
            sb.AppendLine("Clip: " + clip.name +
                " | length=" + clip.length.ToString("F4") +
                "s | FPS=" + clip.frameRate.ToString("F3"));

            var bindings = AnimationUtility.GetCurveBindings(clip);
            var uniquePaths = new HashSet<string>(StringComparer.Ordinal);
            foreach (EditorCurveBinding binding in bindings)
                uniquePaths.Add(binding.path ?? "");
            int hit = 0;
            var missing = new List<string>();
            foreach (string path in uniquePaths.OrderBy(p => p, StringComparer.Ordinal))
            {
                if (string.IsNullOrEmpty(path) || root.transform.Find(path) != null)
                    hit++;
                else
                    missing.Add(path);
            }
            sb.AppendLine("Unity AnimationUtility curve bindings: " + bindings.Length);
            sb.AppendLine("Unique binding paths: " + uniquePaths.Count +
                " | Paths resolved relative to MODEL ROOT: " + hit +
                " | Missing: " + missing.Count);
            if (bindings.Length == 0)
                sb.AppendLine("WARNING: Unity reports ZERO curve bindings for this clip. " +
                    "The submitted park clip also has an empty serialized genericBindings table. " +
                    "Try 'Refresh clip import', then reanalyze; if still empty, " +
                    "regenerate the .anim in Unity and inspect Editor Console.");

            Transform rig = root.transform.Find("Skell_Rig");
            sb.AppendLine("Skell_Rig direct child: " + (rig == null ? "MISSING" : "present"));
            if (rig == null)
                sb.AppendLine("ERROR: Selected GameObject is not the imported CAMDO root. " +
                    "Select its PARENT which directly contains Skell_Rig and model_###.");
            if (missing.Count > 0)
            {
                foreach (string path in missing.Take(12))
                    sb.AppendLine("  UNRESOLVED: " + path);
                if (missing.Count > 12)
                    sb.AppendLine("  ... " + (missing.Count - 12) +
                        " more binding paths not found.");
            }

            Animator rootAnimator = root.GetComponent<Animator>();
            Animator[] allAnimators = root.GetComponentsInChildren<Animator>(true);
            sb.AppendLine("Animator on model root: " +
                (rootAnimator == null ? "NO" : "YES"));
            sb.AppendLine("Animator total under root: " + allAnimators.Length);
            foreach (Animator a in allAnimators.Take(10))
            {
                string where = RelativePath(root.transform, a.transform);
                sb.AppendLine("  Animator at " + where +
                    " | enabled=" + a.enabled +
                    " | active=" + a.gameObject.activeInHierarchy +
                    " | controller=" +
                    (a.runtimeAnimatorController == null ?
                        "(none)" : a.runtimeAnimatorController.name) +
                    " | cullingMode=" + a.cullingMode);
            }
            if (rootAnimator == null)
                sb.AppendLine("ERROR: Put the Animator on the imported CAMDO ROOT, " +
                    "not directly on Skell_Rig, and assign a Controller with the clip.");
            else if (rootAnimator.runtimeAnimatorController == null)
                sb.AppendLine("ERROR: Root Animator has no RuntimeAnimatorController.");
            else if (!rootAnimator.runtimeAnimatorController.animationClips
                .Any(c => c == clip))
                sb.AppendLine("WARNING: Root Animator's controller does not reference " +
                    "the selected AnimationClip.");
            if (allAnimators.Any(a => a != rootAnimator))
                sb.AppendLine("WARNING: Nested Animators can take ownership of the " +
                    "same bone transforms and interfere with the root Animator.");

            SkinnedMeshRenderer[] skins =
                root.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            MeshRenderer[] staticMeshes =
                root.GetComponentsInChildren<MeshRenderer>(true);
            sb.AppendLine("SkinnedMeshRenderer count: " + skins.Length +
                " | Static MeshRenderer count: " + staticMeshes.Length);
            if (skins.Length == 0)
                sb.AppendLine("ERROR: There are NO skinned mesh renderers. " +
                    "Bones may animate but MODEL geometry cannot deform. " +
                    "In CAMDO Rig tab enable Import Skell HKT Hierarchy and " +
                    "Import Skell Bone Weights, then reimport.");
            if (staticMeshes.Length > 0)
                sb.AppendLine("WARNING: " + staticMeshes.Length +
                    " static draw meshes cannot deform with the skeleton; " +
                    "inspect Skell skin fallback warnings in importer.");
            foreach (SkinnedMeshRenderer skin in skins.Take(15))
            {
                int assigned = skin.bones == null ? 0 : skin.bones.Length;
                int attached = 0;
                if (skin.bones != null && rig != null)
                    foreach (Transform bone in skin.bones)
                        if (bone != null && (bone == rig || bone.IsChildOf(rig)))
                            attached++;
                int weightCount = skin.sharedMesh == null ? 0 :
                    skin.sharedMesh.boneWeights.Length;
                sb.AppendLine("  SKIN " + RelativePath(root.transform, skin.transform) +
                    ": assigned bones=" + assigned +
                    ", inside Skell_Rig=" + attached +
                    ", mesh vertex weights=" + weightCount +
                    ", rootBone=" +
                    (skin.rootBone == null ? "(null)" : skin.rootBone.name));
                if (assigned == 0 || attached != assigned || weightCount == 0)
                    sb.AppendLine("  ERROR: Skin has missing/unlinked bones or missing weights.");
            }

            if (bindings.Length > 0 && hit == uniquePaths.Count &&
                rootAnimator != null &&
                rootAnimator.runtimeAnimatorController != null &&
                skins.Length > 0)
                sb.AppendLine("Binding/path prerequisites look correct. " +
                    "If the mesh remains static, test at 50% preview and " +
                    "inspect Animator state/culling, renderer bones and weight data.");
            sb.AppendLine("The diagnostic does not change HKT, CAMDO, prefab, " +
                "Animator, controller, .anim or material assets.");
            return sb.ToString();
        }

        private void RefreshClip()
        {
            StopPreview();
            if (motion == null) return;
            string path = AssetDatabase.GetAssetPath(motion);
            if (string.IsNullOrEmpty(path)) return;
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            motion = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            Analyze();
        }

        private void Preview()
        {
            try
            {
                if (modelRoot == null || motion == null || !modelRoot.scene.IsValid())
                    throw new InvalidOperationException(
                        "Use a CAMDO scene instance for temporary pose preview.");
                if (!AnimationMode.InAnimationMode())
                {
                    AnimationMode.StartAnimationMode();
                    ownsPreview = true;
                }
                AnimationMode.BeginSampling();
                AnimationMode.SampleAnimationClip(modelRoot, motion,
                    motion.length * 0.5f);
                AnimationMode.EndSampling();
                SceneView.RepaintAll();
                Analyze();
                report += "\nTemporary preview sampled at clip midpoint. " +
                    "Click 'Stop temporary preview' to restore the scene.";
            }
            catch (Exception ex)
            {
                report = "PREVIEW FAILED: " + ex.Message + "\n" + report;
                StopPreview();
            }
        }

        private void StopPreview()
        {
            if (!ownsPreview) return;
            if (AnimationMode.InAnimationMode())
                AnimationMode.StopAnimationMode();
            ownsPreview = false;
            SceneView.RepaintAll();
        }

        private static string RelativePath(Transform root, Transform t)
        {
            var names = new List<string>();
            while (t != null && t != root)
            {
                names.Insert(0, t.name);
                t = t.parent;
            }
            return t == root ? (names.Count == 0 ? "(MODEL ROOT)" :
                string.Join("/", names.ToArray())) : "(outside root)";
        }

        private void OnDisable() { StopPreview(); }
    }
}
#endif
