// Converts extracted Wii U .camdo files to GLB using the external xc3_gltf CLI.
// Unity glTFast (if installed) imports generated GLB as a native model asset.
// No copyrighted game data or third party converter executable are bundled.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace MusafirEx.XBX2Unity.Editor
{
    public sealed class XBX2UnityModelWindow : EditorWindow
    {
        private const string Pref = "MusafirEx.XBX2Unity.Model.";
        private const string DownloadUrl = "https://github.com/ScanMountGoat/xc3_lib/releases";
        private const int Limit = 1000;
        private readonly List<string> models = new List<string>();
        private readonly Queue<string> work = new Queue<string>();
        private readonly Queue<string> lines = new Queue<string>();
        private readonly object linesSync = new object();
        private readonly StringBuilder log = new StringBuilder();
        private Process process;
        private string selectionPath, converterExe, currentSource, currentTemp, currentDest, currentError;
        private string currentLogDir, status = "Ready";
        private bool stopped, frameInScene = true, runningBatch, firstPreviewCreated;
        private int modelIndex, finished, failed, skipped, queuedLogDropped;
        private Vector2 logScroll;
        
        private static string Root { get { return Directory.GetParent(Application.dataPath).FullName; } }
        private bool Busy { get { return process != null || work.Count > 0; } }

        [MenuItem("Tools/XBX2Unity/CAMDO Model Viewer")]
        public static void Open()
        {
            var w = GetWindow<XBX2UnityModelWindow>("XBX2Unity Models");
            w.Show();
        }

        [MenuItem("Assets/XBX2Unity/Convert and View CAMDO", false, 2102)]
        public static void OpenSelected()
        {
            var w = GetWindow<XBX2UnityModelWindow>("XBX2Unity Models");
            w.UseSelectedAsset();
            w.Show();
        }

        [MenuItem("Assets/XBX2Unity/Convert and View CAMDO", true)]
        private static bool ValidateSelected()
        {
            foreach (UnityEngine.Object asset in Selection.objects)
            {
                string p = AssetDatabase.GetAssetPath(asset);
                if (AssetDatabase.IsValidFolder(p) || p.EndsWith(".camdo", StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        private void OnEnable()
        {
            converterExe = EditorPrefs.GetString(Pref + "converter", "");
            selectionPath = EditorPrefs.GetString(Pref + "selection", "Assets/XBX/ma9990");
            frameInScene = EditorPrefs.GetBool(Pref + "frame", true);
            if (Selection.activeObject != null)
            {
                string p = AssetDatabase.GetAssetPath(Selection.activeObject);
                if (AssetDatabase.IsValidFolder(p) || p.EndsWith(".camdo", StringComparison.OrdinalIgnoreCase))
                    selectionPath = p;
            }
            Scan();
            EditorApplication.update += Poll;
        }

        private void OnDisable()
        {
            EditorApplication.update -= Poll;
            if (process != null)
            {
                try { if (!process.HasExited) process.Kill(); } catch { }
                try { process.Dispose(); } catch { }
                process = null;
            }
            work.Clear();
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Wii U CAMDO → GLB → Unity Preview", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Requires ScanMountGoat xc3_gltf.exe (external). For native Unity model import, install com.unity.cloud.gltfast (already present in RTS project). CAMDO files stay unchanged.", MessageType.Info);

            EditorGUI.BeginDisabledGroup(Busy);
            EditorGUILayout.LabelField("Converter executable: xc3_gltf.exe", EditorStyles.boldLabel);
            EditorGUILayout.BeginHorizontal();
            converterExe = EditorGUILayout.TextField(converterExe ?? "");
            if (GUILayout.Button("Browse", GUILayout.Width(72)))
            {
                string f = EditorUtility.OpenFilePanel("Select xc3_gltf.exe", Root, "exe");
                if (!string.IsNullOrEmpty(f)) converterExe = f;
            }
            EditorGUILayout.EndHorizontal();
            if (GUILayout.Button("Download xc3_gltf from xc3_lib releases", EditorStyles.linkLabel))
                Application.OpenURL(DownloadUrl);

            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("Selected .camdo / source folder", EditorStyles.boldLabel);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.TextField(selectionPath ?? "");
            if (GUILayout.Button("Use Selection", GUILayout.Width(115))) UseSelectedAsset();
            if (GUILayout.Button("Browse", GUILayout.Width(72)))
            {
                string p = EditorUtility.OpenFilePanel("Select CAMDO file", Root, "camdo");
                if (!string.IsNullOrEmpty(p))
                {
                    string absolute = Path.GetFullPath(p);
                    string assetsDir = Path.GetFullPath(Application.dataPath).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                    if (absolute.StartsWith(assetsDir, StringComparison.OrdinalIgnoreCase))
                        selectionPath = "Assets/" + absolute.Substring(assetsDir.Length).Replace('\\', '/');
                    else
                        selectionPath = absolute;
                    Scan();
                }
            }
            EditorGUILayout.EndHorizontal();
            if (GUILayout.Button("Rescan models")) Scan();

            EditorGUILayout.LabelField("Detected CAMDO models: " + models.Count, EditorStyles.boldLabel);
            if (models.Count > 0)
            {
                modelIndex = Mathf.Clamp(modelIndex, 0, models.Count - 1);
                var options = models.Select(x => Path.GetFileName(x) + "  (" + ShortName(x) + ")").ToArray();
                modelIndex = EditorGUILayout.Popup("Model", modelIndex, options);
                string model = models[modelIndex];
                EditorGUILayout.LabelField("Model size: " + new FileInfo(model).Length.ToString("N0") + " bytes", EditorStyles.miniLabel);
                string companion = Path.ChangeExtension(model, ".casmt");
                if (File.Exists(companion))
                    EditorGUILayout.LabelField("Companion .casmt: found", EditorStyles.miniLabel);
                else
                    EditorGUILayout.HelpBox("No matching .casmt alongside this model. Some CAMDO assets (e.g. Skybox0 or terrainLOD) may work without it; try conversion.", MessageType.Warning);
            }
            frameInScene = EditorGUILayout.ToggleLeft("Instantiate the imported model in the Scene and frame it", frameInScene);

            EditorGUILayout.BeginHorizontal();
            EditorGUI.BeginDisabledGroup(models.Count == 0);
            if (GUILayout.Button("Convert Selected & View", GUILayout.Height(30)))
                Begin(false);
            if (GUILayout.Button("Convert Up To 20 Models", GUILayout.Height(30)))
            {
                if (EditorUtility.DisplayDialog("Batch conversion", "Convert up to 20 CAMDO models? This may take significant disk space and processing time.", "Convert", "Cancel"))
                    Begin(true);
            }
            EditorGUI.EndDisabledGroup();
            EditorGUILayout.EndHorizontal();
            EditorGUI.EndDisabledGroup();

            if (Busy && GUILayout.Button("Stop Current / Remaining", GUILayout.Height(27))) Stop();
            EditorGUILayout.LabelField("Status: " + status, EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Converted: " + finished + "   Failed: " + failed + "   Skipped: " + skipped, EditorStyles.miniLabel);
            EditorGUILayout.HelpBox(
                "Output: Assets/XBX_Imported/<original relative path>.glb. Click the GLB model in Project to inspect meshes and materials, or drag it into Scene. Texture/material quality depends on xc3_gltf and glTFast.", MessageType.None);
            EditorGUILayout.LabelField("Process Log", EditorStyles.boldLabel);
            logScroll = EditorGUILayout.BeginScrollView(logScroll, GUILayout.MinHeight(120));
            EditorGUILayout.TextArea(log.ToString(), GUILayout.ExpandHeight(true));
            EditorGUILayout.EndScrollView();
            if (GUILayout.Button("Copy Log")) EditorGUIUtility.systemCopyBuffer = log.ToString();

            if (Event.current.type == EventType.Layout)
            {
                EditorPrefs.SetString(Pref + "converter", converterExe ?? "");
                EditorPrefs.SetString(Pref + "selection", selectionPath ?? "");
                EditorPrefs.SetBool(Pref + "frame", frameInScene);
            }
        }

        private static string ShortName(string full)
        {
            string path = full.Replace('\\', '/');
            int at = path.IndexOf("/XBX/", StringComparison.OrdinalIgnoreCase);
            return at >= 0 ? path.Substring(at + 1) : Path.GetFileName(full);
        }

        private void UseSelectedAsset()
        {
            foreach (UnityEngine.Object asset in Selection.objects)
            {
                string p = AssetDatabase.GetAssetPath(asset);
                if (AssetDatabase.IsValidFolder(p) || p.EndsWith(".camdo", StringComparison.OrdinalIgnoreCase))
                {
                    selectionPath = p;
                    Scan();
                    return;
                }
            }
            Append("Please select a .camdo file or folder in the Unity Project window.");
        }

        private void Scan()
        {
            models.Clear();
            if (string.IsNullOrEmpty(selectionPath)) return;
            try
            {
                string absolute = selectionPath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase) ||
                                  string.Equals(selectionPath, "Assets", StringComparison.OrdinalIgnoreCase)
                    ? Path.GetFullPath(Path.Combine(Root, selectionPath))
                    : Path.GetFullPath(selectionPath);
                if (File.Exists(absolute) && absolute.EndsWith(".camdo", StringComparison.OrdinalIgnoreCase))
                    models.Add(absolute);
                else if (Directory.Exists(absolute))
                {
                    foreach (string file in Directory.EnumerateFiles(absolute, "*", SearchOption.AllDirectories))
                    {
                        if (file.EndsWith(".camdo", StringComparison.OrdinalIgnoreCase))
                            models.Add(file);
                        if (models.Count == Limit) break;
                    }
                    models.Sort(StringComparer.OrdinalIgnoreCase);
                }
                modelIndex = Mathf.Clamp(modelIndex, 0, Mathf.Max(0, models.Count - 1));
                Append("Detected " + models.Count + " CAMDO files" + (models.Count == Limit ? " (scan limit)" : "") + ".");
            }
            catch (Exception e) { Append("Scan failed: " + e.Message); }
        }

        private bool ValidateSetup()
        {
            if (Application.platform != RuntimePlatform.WindowsEditor)
            {
                EditorUtility.DisplayDialog("Windows required", "This build expects a Windows xc3_gltf.exe binary.", "OK");
                return false;
            }
            if (string.IsNullOrEmpty(converterExe) || !File.Exists(converterExe) ||
                !string.Equals(Path.GetFileName(converterExe), "xc3_gltf.exe", StringComparison.OrdinalIgnoreCase))
            {
                EditorUtility.DisplayDialog("Missing converter", "Select xc3_gltf.exe from the xc3_lib Windows release. Do not choose XenobladeToolset or casmExtract.", "OK");
                return false;
            }
            if (models.Count == 0) { EditorUtility.DisplayDialog("No CAMDO", "Choose a .camdo model or folder.", "OK"); return false; }
            return true;
        }

        private void Begin(bool batch)
        {
            if (Busy || !ValidateSetup()) return;
            work.Clear();
            stopped = false; runningBatch = batch;
            finished = failed = skipped = 0;
            firstPreviewCreated = false;
            if (batch)
            {
                foreach (string model in models.Take(20)) work.Enqueue(model);
            }
            else work.Enqueue(models[modelIndex]);
            Append("Queued " + work.Count + " model(s).");
            RunNext();
        }

        private static string Quoted(string x) { return "\"" + x.Replace("\"", "") + "\""; }

        private static string DestinationPath(string model)
        {
            string relative = Path.GetFullPath(model);
            string assets = Path.GetFullPath(Application.dataPath).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!relative.StartsWith(assets, StringComparison.OrdinalIgnoreCase))
                throw new IOException("Models must be inside the project's Assets directory.");
            relative = relative.Substring(assets.Length).Replace('\\', '/');
            string noExtension = relative.Substring(0, relative.Length - ".camdo".Length);
            return "Assets/XBX_Imported/" + noExtension + ".glb";
        }

        private void RunNext()
        {
            if (stopped || work.Count == 0)
            {
                status = "Finished. Success: " + finished + ", failed: " + failed + ", skipped: " + skipped;
                Append(status);
                Repaint();
                return;
            }
            currentSource = work.Dequeue();
            currentError = "";
            try
            {
                currentDest = DestinationPath(currentSource);
                currentLogDir = Path.Combine(Root, "XBX_Exports", "ModelConversion", Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(currentLogDir);
                currentTemp = Path.Combine(currentLogDir, Path.GetFileNameWithoutExtension(currentSource) + ".glb");
                var args = Quoted(currentSource) + " --output " + Quoted(currentTemp);
                var info = new ProcessStartInfo
                {
                    FileName = Path.GetFullPath(converterExe),
                    Arguments = args,
                    WorkingDirectory = Path.GetDirectoryName(Path.GetFullPath(converterExe)),
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };
                process = new Process { StartInfo = info };
                process.OutputDataReceived += (sender, e) => QueueLine(e.Data, false);
                process.ErrorDataReceived += (sender, e) => QueueLine(e.Data, true);
                status = "Converting: " + ShortName(currentSource);
                Append("Running: " + Path.GetFileName(converterExe) + " " + args);
                if (!process.Start()) throw new IOException("Process.Start returned false.");
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
            }
            catch (Exception e)
            {
                failed++;
                Append("ERROR " + ShortName(currentSource) + ": " + e.Message);
                if (process != null) { try { process.Dispose(); } catch { } process = null; }
                SaveLog();
                RunNext();
            }
        }

        private void QueueLine(string line, bool err)
        {
            if (line == null) return;
            lock (linesSync)
            {
                if (lines.Count < 10000) lines.Enqueue((err ? "[stderr] " : "") + line);
                else queuedLogDropped++;
            }
        }

        private void Drain()
        {
            lock (linesSync)
            {
                while (lines.Count != 0) Append(lines.Dequeue());
            }
        }

        private static bool IsGlb(string path)
        {
            if (!File.Exists(path)) return false;
            using (var f = File.OpenRead(path))
            {
                if (f.Length < 20) return false;
                byte[] bytes = new byte[12];
                if (f.Read(bytes, 0, bytes.Length) != bytes.Length) return false;
                // glTF binary container magic "glTF" and version 2
                return bytes[0] == (byte)'g' && bytes[1] == (byte)'l' &&
                       bytes[2] == (byte)'T' && bytes[3] == (byte)'F' &&
                       BitConverter.ToUInt32(bytes, 4) == 2 &&
                       BitConverter.ToUInt32(bytes, 8) == f.Length;
            }
        }

        private void Poll()
        {
            Drain();
            if (process == null) return;
            try
            {
                if (!process.HasExited) return;
                process.WaitForExit();
                Drain();
                int exit = process.ExitCode;
                process.Dispose();
                process = null;
                if (queuedLogDropped > 0) { Append("Log lines dropped: " + queuedLogDropped); queuedLogDropped = 0; }
                if (stopped) Append("Cancelled; no generated asset imported.");
                else if (exit == 0 && IsGlb(currentTemp))
                {
                    try
                    {
                        string fullDest = Path.Combine(Root, currentDest);
                        Directory.CreateDirectory(Path.GetDirectoryName(fullDest));
                        File.Copy(currentTemp, fullDest, true);
                        AssetDatabase.ImportAsset(currentDest, ImportAssetOptions.ForceSynchronousImport);
                        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(currentDest);
                        UnityEngine.Object item = (UnityEngine.Object)prefab ?? AssetDatabase.LoadMainAssetAtPath(currentDest);
                        if (item != null)
                        {
                            EditorGUIUtility.PingObject(item);
                            if (frameInScene && !firstPreviewCreated && prefab != null)
                            {
                                var go = PrefabUtility.InstantiatePrefab(prefab) as GameObject;
                                if (go != null)
                                {
                                    Undo.RegisterCreatedObjectUndo(go, "Preview Xenoblade CAMDO");
                                    Selection.activeGameObject = go;
                                    if (SceneView.lastActiveSceneView != null)
                                        SceneView.lastActiveSceneView.FrameSelected();
                                    firstPreviewCreated = true;
                                }
                            }
                        }
                        if (prefab == null)
                            Append("GLB copied but no GameObject imported. Install Unity glTFast and reimport: " + currentDest);
                        else Append("VIEWABLE MODEL: " + currentDest);
                        finished++;
                    }
                    catch (Exception e)
                    {
                        failed++;
                        Append("Unity import failed: " + e.Message);
                    }
                }
                else
                {
                    failed++;
                    Append("Conversion failed: " + ShortName(currentSource) + " (exit " + exit + ", GLB valid: " + IsGlb(currentTemp) + ")");
                }
                SaveLog();
                RunNext();
            }
            catch (Exception e)
            {
                failed++;
                Append("Process exception: " + e.Message);
                try { process.Dispose(); } catch { } process = null;
                SaveLog();
                RunNext();
            }
        }

        private void SaveLog()
        {
            try
            {
                if (!string.IsNullOrEmpty(currentLogDir) && Directory.Exists(currentLogDir))
                    File.WriteAllText(Path.Combine(currentLogDir, "conversion.log"), log.ToString(), Encoding.UTF8);
            }
            catch (Exception e) { UnityEngine.Debug.LogWarning("XBX2Unity model log: " + e.Message); }
        }

        private void Stop()
        {
            stopped = true;
            work.Clear();
            if (process != null)
            {
                try { if (!process.HasExited) process.Kill(); }
                catch (Exception e) { Append("Stop failed: " + e.Message); }
            }
        }

        private void Append(string message)
        {
            log.AppendLine("[" + DateTime.Now.ToString("HH:mm:ss") + "] " + message);
            if (log.Length > 50000) log.Remove(0, log.Length - 35000);
            Repaint();
        }
    }
}
