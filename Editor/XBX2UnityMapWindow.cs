// XBX2Unity v0.2.0 — editor-only CASM extraction front end.
// Does NOT decode meshes or CANV content. Requires casmExtract.exe separately.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace MusafirEx.XBX2Unity.Editor
{
    public sealed class XBX2UnityMapWindow : EditorWindow
    {
        private const string Pref = "MusafirEx.XBX2Unity.";
        private const string ToolUrl = "https://github.com/PredatorCZ/XenoToolset";
        private static readonly string[] Extensions = { ".casmhd", ".casmda", ".canvhe", ".canvda" };
        private sealed class Group
        {
            public string key, name, casmhd, casmda, canvhe, canvda;
            public bool selected = true;
            public IEnumerable<string> Files
            {
                get
                {
                    if (casmhd != null) yield return casmhd;
                    if (casmda != null) yield return casmda;
                    if (canvhe != null) yield return canvhe;
                    if (canvda != null) yield return canvda;
                }
            }
        }

        private readonly List<Group> groups = new List<Group>();
        private readonly Queue<Group> queue = new Queue<Group>();
        private readonly Queue<string> lines = new Queue<string>();
        private readonly object sync = new object();
        private readonly StringBuilder log = new StringBuilder();
        private readonly HashSet<string> stagedInputs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private string source, tool, output, activeDirectory, activeName;
        private Process process;
        private bool png = true, blue, allowEmptyData, cancel;
        private int ok, errors, skipped;
        private Vector2 groupsScroll, logScroll;

        private static string Root { get { return Directory.GetParent(Application.dataPath).FullName; } }
        private bool Busy { get { return process != null || queue.Count > 0; } }

        [MenuItem("Tools/XBX2Unity/Map Extractor")]
        public static void Open()
        {
            GetWindow<XBX2UnityMapWindow>("XBX2Unity").Show();
        }

        [MenuItem("Assets/XBX2Unity/Inspect Selected Files", false, 2100)]
        public static void InspectSelection()
        {
            var window = GetWindow<XBX2UnityMapWindow>("XBX2Unity");
            window.UseSelection();
            window.Show();
        }

        private void OnEnable()
        {
            source = EditorPrefs.GetString(Pref + "source", Path.Combine(Root, "Assets/XBX"));
            tool = EditorPrefs.GetString(Pref + "tool", "");
            output = EditorPrefs.GetString(Pref + "output", Path.Combine(Root, "XBX_Exports"));
            UnityEditor.EditorApplication.update += Tick;
            Scan();
        }

        private void OnDisable()
        {
            UnityEditor.EditorApplication.update -= Tick;
            // Never leave an untracked extractor running after window closure/recompile.
            if (process != null)
            {
                try { if (!process.HasExited) process.Kill(); } catch { }
                try { process.Dispose(); } catch { }
                process = null;
                Append("Cancelled: editor window closed or scripts recompiled.");
                if (!string.IsNullOrEmpty(activeDirectory)) SaveLog(activeDirectory);
            }
            queue.Clear();
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Xenoblade Chronicles X (Wii U) — Map Assets", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("CASMHD/CASMDA: extraction via external casmExtract.exe. CANVHE/CANVDA: header audit only. No native Mesh/Texture importer is included.", MessageType.Info);
            EditorGUI.BeginDisabledGroup(Busy);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Use Project Selection")) UseSelection();
            if (GUILayout.Button("Browse Source"))
            {
                string selected = EditorUtility.OpenFolderPanel("XBX source folder", source, "");
                if (!string.IsNullOrEmpty(selected)) { source = selected; Scan(); }
            }
            if (GUILayout.Button("Rescan", GUILayout.Width(80))) Scan();
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.SelectableLabel(source ?? "", EditorStyles.textField, GUILayout.Height(20));
            EditorGUILayout.LabelField("External casmExtract.exe", EditorStyles.boldLabel);
            EditorGUILayout.BeginHorizontal();
            tool = EditorGUILayout.TextField(tool ?? "");
            if (GUILayout.Button("Browse", GUILayout.Width(72)))
            {
                string selected = EditorUtility.OpenFilePanel("Choose casmExtract.exe", Root, "exe");
                if (!string.IsNullOrEmpty(selected)) tool = selected;
            }
            EditorGUILayout.EndHorizontal();
            if (GUILayout.Button("XenoToolset documentation", EditorStyles.linkLabel)) Application.OpenURL(ToolUrl);
            EditorGUILayout.LabelField("Export folder (outside Assets recommended)", EditorStyles.boldLabel);
            EditorGUILayout.BeginHorizontal();
            output = EditorGUILayout.TextField(output ?? "");
            if (GUILayout.Button("Browse", GUILayout.Width(72)))
            {
                string selected = EditorUtility.OpenFolderPanel("Choose output folder", output, "");
                if (!string.IsNullOrEmpty(selected)) output = selected;
            }
            EditorGUILayout.EndHorizontal();
            png = EditorGUILayout.ToggleLeft("-u Export textures as PNG", png);
            blue = EditorGUILayout.ToggleLeft("-b Reconstruct BC5 normal blue channel", blue);
            allowEmptyData = EditorGUILayout.ToggleLeft("Advanced: attempt CASM when CASMDA is empty", allowEmptyData);
            EditorGUI.EndDisabledGroup();
            if (Event.current.type == EventType.Layout)
            {
                EditorPrefs.SetString(Pref + "source", source ?? "");
                EditorPrefs.SetString(Pref + "tool", tool ?? "");
                EditorPrefs.SetString(Pref + "output", output ?? "");
            }

            EditorGUILayout.Space(5);
            EditorGUILayout.LabelField("Detected groups: " + groups.Count, EditorStyles.boldLabel);
            groupsScroll = EditorGUILayout.BeginScrollView(groupsScroll, GUILayout.Height(Mathf.Clamp(groups.Count * 114, 95, 260)));
            foreach (Group g in groups)
            {
                EditorGUILayout.BeginVertical("box");
                EditorGUI.BeginDisabledGroup(Busy);
                g.selected = EditorGUILayout.ToggleLeft(g.name, g.selected, EditorStyles.boldLabel);
                EditorGUI.EndDisabledGroup();
                DrawFile("CASMHD", g.casmhd);
                DrawFile("CASMDA", g.casmda);
                DrawFile("CANVHE", g.canvhe);
                DrawFile("CANVDA", g.canvda);
                if (g.casmhd != null && (g.casmda == null || IsEmpty(g.casmda)))
                    EditorGUILayout.HelpBox("CASMDA missing/0 bytes; verify extracted game data. No assumption is made about why.", MessageType.Warning);
                EditorGUILayout.EndVertical();
            }
            EditorGUILayout.EndScrollView();
            EditorGUILayout.BeginHorizontal();
            EditorGUI.BeginDisabledGroup(Busy || groups.Count == 0);
            if (GUILayout.Button("Write Binary Audit", GUILayout.Height(30))) Audit();
            if (GUILayout.Button("Extract Selected CASM", GUILayout.Height(30))) Extract();
            EditorGUI.EndDisabledGroup();
            if (Busy && GUILayout.Button("Stop", GUILayout.Width(70), GUILayout.Height(30))) Stop();
            EditorGUILayout.EndHorizontal();
            if (Busy) EditorGUILayout.HelpBox("Extracting: " + activeName + ". Closing the window or recompiling scripts will cancel the external process.", MessageType.Warning);
            EditorGUILayout.LabelField("Log", EditorStyles.boldLabel);
            logScroll = EditorGUILayout.BeginScrollView(logScroll, GUILayout.MinHeight(115));
            EditorGUILayout.TextArea(log.ToString(), GUILayout.ExpandHeight(true));
            EditorGUILayout.EndScrollView();
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Copy Log")) EditorGUIUtility.systemCopyBuffer = log.ToString();
            if (GUILayout.Button("Open Output") && Directory.Exists(output)) EditorUtility.RevealInFinder(output);
            EditorGUILayout.EndHorizontal();
        }

        private static void DrawFile(string type, string path)
        {
            string msg = path == null ? "missing" : Size(new FileInfo(path).Length);
            EditorGUILayout.LabelField("  " + type + ": " + msg, EditorStyles.miniLabel);
        }
        private static bool IsEmpty(string path) { return path == null || !File.Exists(path) || new FileInfo(path).Length == 0; }
        private static string Size(long n)
        {
            return n < 1024 ? n + " B" : n < 1048576 ? (n / 1024d).ToString("F1") + " KiB" : (n / 1048576d).ToString("F1") + " MiB";
        }

        private void UseSelection()
        {
            foreach (UnityEngine.Object asset in Selection.objects)
            {
                string relative = AssetDatabase.GetAssetPath(asset);
                if (string.IsNullOrEmpty(relative)) continue;
                string full = Path.GetFullPath(Path.Combine(Root, relative));
                source = Directory.Exists(full) ? full : Path.GetDirectoryName(full);
                Scan();
                return;
            }
            Append("Select a folder or one .casm*/.canv* file in Unity Project view.");
        }

        private void Scan()
        {
            groups.Clear();
            if (string.IsNullOrEmpty(source) || !Directory.Exists(source)) { Append("Source folder not found."); return; }
            var map = new Dictionary<string, Group>(StringComparer.OrdinalIgnoreCase);
            int seen = 0;
            foreach (string file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
            {
                if (++seen > 30000) { Append("Scan limit (30,000 files) reached."); break; }
                string ext = Path.GetExtension(file).ToLowerInvariant();
                if (Array.IndexOf(Extensions, ext) < 0) continue;
                string stem = Path.GetFileNameWithoutExtension(file);
                string parent = Path.GetDirectoryName(file);
                string key = Path.Combine(parent, stem);
                Group g;
                if (!map.TryGetValue(key, out g))
                {
                    string sub = parent.Substring(Path.GetFullPath(source).TrimEnd(Path.DirectorySeparatorChar).Length).TrimStart(Path.DirectorySeparatorChar);
                    g = new Group { key = key, name = string.IsNullOrEmpty(sub) ? stem : sub + "/" + stem };
                    map.Add(key, g);
                }
                switch (ext)
                {
                    case ".casmhd": g.casmhd = file; break;
                    case ".casmda": g.casmda = file; break;
                    case ".canvhe": g.canvhe = file; break;
                    case ".canvda": g.canvda = file; break;
                }
            }
            groups.AddRange(map.Values.OrderBy(g => g.name, StringComparer.OrdinalIgnoreCase));
            Append("Scanned " + seen + " files; detected " + groups.Count + " map group(s).");
            Repaint();
        }

        private string ExportRoot()
        {
            try
            {
                if (string.IsNullOrWhiteSpace(output)) throw new IOException("Output path is empty.");
                string path = Path.GetFullPath(output);
                string original = Path.GetFullPath(source ?? Root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                if (path.StartsWith(original, StringComparison.OrdinalIgnoreCase))
                    throw new IOException("Output must not be placed inside the source folder.");
                string assets = Path.GetFullPath(Application.dataPath).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                if (path.StartsWith(assets, StringComparison.OrdinalIgnoreCase) &&
                    !EditorUtility.DisplayDialog("Export into Assets?", "Large outputs can trigger expensive Unity imports. Continue?", "Continue", "Cancel"))
                    return null;
                Directory.CreateDirectory(path);
                return path;
            }
            catch (Exception e) { EditorUtility.DisplayDialog("Invalid output", e.Message, "OK"); return null; }
        }

        private void Audit()
        {
            string folder = ExportRoot();
            if (folder == null) return;
            var report = new StringBuilder("XBX2Unity v0.2.0 binary audit (read only)\n");
            report.AppendLine("UTC: " + DateTime.UtcNow.ToString("u"));
            report.AppendLine("Source: " + source);
            report.AppendLine("First 64 bytes only; not a validated binary format decode.");
            foreach (Group g in groups.Where(x => x.selected))
            {
                report.AppendLine("\n=== " + g.name + " ===");
                foreach (string f in g.Files)
                {
                    var info = new FileInfo(f);
                    report.AppendLine(info.Name + " (" + info.Length + " bytes)");
                    if (info.Length == 0) { report.AppendLine("EMPTY FILE"); continue; }
                    try
                    {
                        byte[] b = new byte[64];
                        int n;
                        using (var stream = File.OpenRead(f)) n = stream.Read(b, 0, b.Length);
                        report.AppendLine("HEX: " + BitConverter.ToString(b, 0, n));
                        report.AppendLine("ASCII: " + new string(b.Take(n).Select(x => x >= 32 && x < 127 ? (char)x : '.').ToArray()));
                    }
                    catch (Exception e) { report.AppendLine("READ ERROR: " + e.Message); }
                }
                report.AppendLine("CASM: " + (g.casmhd == null ? "missing header" : IsEmpty(g.casmda) ? "data missing or empty" : "pair found"));
                report.AppendLine("CANV: audit-only, no decoder in this release.");
            }
            string filename = Path.Combine(folder, "xbx_audit_" + DateTime.UtcNow.ToString("yyyyMMdd_HHmmss") + ".txt");
            File.WriteAllText(filename, report.ToString(), Encoding.UTF8);
            Append("Audit written: " + filename);
            EditorUtility.RevealInFinder(filename);
        }

        private void Extract()
        {
            if (Application.platform != RuntimePlatform.WindowsEditor) { EditorUtility.DisplayDialog("Windows required", "This workflow requires the external Windows casmExtract.exe.", "OK"); return; }
            if (string.IsNullOrEmpty(tool) || !File.Exists(tool) ||
                !string.Equals(Path.GetFileName(tool), "casmExtract.exe", StringComparison.OrdinalIgnoreCase))
            { EditorUtility.DisplayDialog("Select casmExtract.exe", "Choose the casmExtract.exe binary from XenoToolset.", "OK"); return; }
            if (ExportRoot() == null) return;
            queue.Clear();
            ok = errors = skipped = 0;
            foreach (Group g in groups.Where(x => x.selected))
            {
                if (g.casmhd == null || (IsEmpty(g.casmda) && !allowEmptyData))
                {
                    skipped++; Append("SKIP " + g.name + ": missing CASMHD or CASMDA empty/missing.");
                    continue;
                }
                queue.Enqueue(g);
            }
            cancel = false;
            if (queue.Count == 0) { Append("No extractable CASM groups. Run Binary Audit first."); return; }
            Append("Queued " + queue.Count + " group(s).");
            RunNext();
        }

        private void RunNext()
        {
            if (cancel || queue.Count == 0)
            {
                activeName = null;
                Append("Completed: success=" + ok + " failed=" + errors + " skipped=" + skipped);
                Repaint();
                return;
            }
            Group g = queue.Dequeue();
            activeName = g.name;
            activeDirectory = Path.Combine(Path.GetFullPath(output), Path.GetFileNameWithoutExtension(g.casmhd) + "_" + DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff") + "_" + Guid.NewGuid().ToString("N").Substring(0, 6));
            stagedInputs.Clear();
            try
            {
                string input = Path.Combine(activeDirectory, "Input");
                Directory.CreateDirectory(input);
                foreach (string file in g.Files)
                {
                    string dest = Path.Combine(input, Path.GetFileName(file));
                    File.Copy(file, dest);
                    stagedInputs.Add(Path.GetFullPath(dest));
                }
                var start = new ProcessStartInfo
                {
                    FileName = tool,
                    Arguments = (png ? "-u " : "") + (blue ? "-b " : "") + "\"" + Path.Combine(input, Path.GetFileName(g.casmhd)) + "\"",
                    WorkingDirectory = activeDirectory,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                process = new Process { StartInfo = start };
                process.OutputDataReceived += (sender, args) => Enqueue(args.Data, false);
                process.ErrorDataReceived += (sender, args) => Enqueue(args.Data, true);
                Append("START " + g.name + " -> " + activeDirectory);
                if (!process.Start()) throw new IOException("Unable to start casmExtract.exe");
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
            }
            catch (Exception e)
            {
                errors++;
                Append("FAILED " + g.name + ": " + e.Message);
                if (process != null) { process.Dispose(); process = null; }
                SaveLog(activeDirectory);
                RunNext();
            }
        }

        private void Enqueue(string value, bool stderr)
        {
            if (value == null) return;
            lock (sync) lines.Enqueue((stderr ? "[stderr] " : "") + value);
        }

        private void Tick()
        {
            lock (sync) while (lines.Count > 0) Append(lines.Dequeue());
            if (process == null) return;
            try
            {
                if (!process.HasExited) return;
                process.WaitForExit();
                int code = process.ExitCode;
                process.Dispose();
                process = null;
                int produced = 0;
                if (Directory.Exists(activeDirectory))
                    foreach (string f in Directory.EnumerateFiles(activeDirectory, "*", SearchOption.AllDirectories))
                        if (!stagedInputs.Contains(Path.GetFullPath(f))) produced++;
                if (code == 0 && produced > 0) { ok++; Append("SUCCESS " + activeName + ": " + produced + " extracted file(s)."); }
                else { errors++; Append("FAILED " + activeName + ": exit=" + code + " outputFiles=" + produced); }
                SaveLog(activeDirectory);
                RunNext();
            }
            catch (Exception e)
            {
                errors++; Append("PROCESS ERROR: " + e.Message);
                try { process.Dispose(); } catch { }
                process = null;
                SaveLog(activeDirectory);
                RunNext();
            }
            Repaint();
        }

        private void Stop()
        {
            cancel = true;
            queue.Clear();
            if (process != null)
            {
                try { if (!process.HasExited) process.Kill(); } catch (Exception e) { Append("Stop error: " + e.Message); }
            }
            Append("Stop requested. Partial output has not been deleted.");
        }

        private void SaveLog(string dir)
        {
            try
            {
                if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                    File.WriteAllText(Path.Combine(dir, "XBX_Extraction_Log.txt"), log.ToString(), Encoding.UTF8);
            }
            catch (Exception e) { UnityEngine.Debug.LogWarning("XBX2Unity: cannot save log: " + e.Message); }
        }

        private void Append(string message)
        {
            log.AppendLine("[" + DateTime.Now.ToString("HH:mm:ss") + "] " + message);
            if (log.Length > 120000) log.Remove(0, log.Length - 90000);
        }
    }
}
