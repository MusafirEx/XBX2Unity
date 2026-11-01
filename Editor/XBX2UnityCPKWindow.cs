// XBX2Unity CPK archive front end. External CriPakTool is not redistributed.
using System;
using System.Collections.Generic;
using System.IO;
using System.Diagnostics;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace MusafirEx.XBX2Unity.Editor
{
    public sealed class XBX2UnityCPKWindow : EditorWindow
    {
        private const string Prefix = "MusafirEx.XBX2Unity.CPK.";
        private const string CriPakUrl = "https://github.com/esperknight/CriPakTools";
        private readonly Queue<string> pendingLines = new Queue<string>();
        private readonly object pendingLock = new object();
        private readonly StringBuilder visibleLog = new StringBuilder();
        private Process process;
        private StreamWriter outputWriter;
        private string cpkPath, extractorPath, exportRoot, currentOutput, latestExtract;
        private string status = "Ready";
        // Tool builds use two incompatible command-line interfaces.
        // 0: CRI CPK Tool (extract_all -p FILE -o DIR)
        // 1: Classic CriPakTool (FILE ALL)
        private static readonly string[] SyntaxOptions =
        {
            "CRI CPK Tool (extract_all -p -o)",
            "Legacy CriPakTool (file ALL)"
        };
        private int cliSyntax;
        private bool cancelling, extracting;
        private int droppedLines;
        private Vector2 scroll;

        private static string ProjectRoot { get { return Directory.GetParent(Application.dataPath).FullName; } }
        private bool Busy { get { return process != null; } }

        [MenuItem("Tools/XBX2Unity/CPK Extractor")]
        public static void Open()
        {
            var window = GetWindow<XBX2UnityCPKWindow>("XBX2Unity CPK");
            window.Show();
        }

        [MenuItem("Assets/XBX2Unity/Extract Selected CPK", false, 2101)]
        public static void OpenSelected()
        {
            var window = GetWindow<XBX2UnityCPKWindow>("XBX2Unity CPK");
            window.UseSelection();
            window.Show();
        }

        [MenuItem("Assets/XBX2Unity/Extract Selected CPK", true)]
        private static bool ValidateMenu()
        {
            foreach (UnityEngine.Object obj in Selection.objects)
            {
                string path = AssetDatabase.GetAssetPath(obj);
                if (!string.IsNullOrEmpty(path) && string.Equals(Path.GetExtension(path), ".cpk", StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        private void OnEnable()
        {
            cpkPath = EditorPrefs.GetString(Prefix + "cpk", "");
            extractorPath = EditorPrefs.GetString(Prefix + "tool", "");
            cliSyntax = Mathf.Clamp(EditorPrefs.GetInt(Prefix + "syntax", 0), 0, 1);
            exportRoot = EditorPrefs.GetString(Prefix + "export", Path.Combine(ProjectRoot, "XBX_Exports"));
            EditorApplication.update += UpdateProcess;
            if (Selection.activeObject != null)
            {
                string selected = AssetDatabase.GetAssetPath(Selection.activeObject);
                if (!string.IsNullOrEmpty(selected) && string.Equals(Path.GetExtension(selected), ".cpk", StringComparison.OrdinalIgnoreCase))
                    UseSelection();
            }
        }

        private void OnDisable()
        {
            EditorApplication.update -= UpdateProcess;
            if (process != null)
            {
                try { if (!process.HasExited) process.Kill(); } catch { }
                try { process.Dispose(); } catch { }
                process = null;
            }
            Drain();
            if (outputWriter != null) { try { outputWriter.Dispose(); } catch { } outputWriter = null; }
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Xenoblade Chronicles X — Wii U CPK Archive", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "CPK is an archive, not a mesh. Choose the matching CriPakTool command-line variant. Extract first; process CASM/CANV separately.",
                MessageType.Info);
            EditorGUI.BeginDisabledGroup(Busy);
            EditorGUILayout.LabelField("CPK file", EditorStyles.boldLabel);
            EditorGUILayout.BeginHorizontal();
            cpkPath = EditorGUILayout.TextField(cpkPath ?? "");
            if (GUILayout.Button("Selected", GUILayout.Width(78))) UseSelection();
            if (GUILayout.Button("Browse", GUILayout.Width(78)))
            {
                string selected = EditorUtility.OpenFilePanel("Choose Wii U CPK", ProjectRoot, "cpk");
                if (!string.IsNullOrEmpty(selected)) SetCpk(selected);
            }
            EditorGUILayout.EndHorizontal();

            if (!string.IsNullOrEmpty(cpkPath) && File.Exists(cpkPath))
            {
                var f = new FileInfo(cpkPath);
                EditorGUILayout.LabelField("File: " + f.Name + "    Size: " + (f.Length / (1024d * 1024d)).ToString("F1") + " MiB", EditorStyles.miniLabel);
                string reason;
                if (!CheckHeader(cpkPath, out reason))
                    EditorGUILayout.HelpBox(reason, MessageType.Error);
                else
                    EditorGUILayout.HelpBox("CPK magic signature confirmed (CPK + space). File table/decompression not yet validated.", MessageType.None);
                if (f.Length >= 2L * 1024 * 1024 * 1024)
                    EditorGUILayout.HelpBox("Large archive (2 GiB+): older CriPakTool builds may fail due to memory/offset limits. Consider a build with explicit large-file support.", MessageType.Warning);
            }
            else
                EditorGUILayout.HelpBox("Select a .cpk file in the Project window or Browse.", MessageType.Warning);

            EditorGUILayout.Space(5);
            EditorGUILayout.LabelField("External CriPakTool.exe", EditorStyles.boldLabel);
            EditorGUILayout.BeginHorizontal();
            extractorPath = EditorGUILayout.TextField(extractorPath ?? "");
            if (GUILayout.Button("Browse", GUILayout.Width(78)))
            {
                string selected = EditorUtility.OpenFilePanel("Choose CriPakTool.exe", ProjectRoot, "exe");
                if (!string.IsNullOrEmpty(selected)) extractorPath = selected;
            }
            EditorGUILayout.EndHorizontal();
            if (GUILayout.Button("CriPakTools source / download", EditorStyles.linkLabel)) Application.OpenURL(CriPakUrl);
            cliSyntax = EditorGUILayout.Popup("Tool command syntax", cliSyntax, SyntaxOptions);
            if (cliSyntax == 0)
                EditorGUILayout.HelpBox("The CRI CPK Tool in your logs supports extract_all and replace, but has no List command. List Archive is disabled for this syntax.", MessageType.Info);
            else
                EditorGUILayout.HelpBox("Legacy CLI: CriPakTool.exe \"archive.cpk\" [ALL]. This variant may support a filename-list mode.", MessageType.Info);

            EditorGUILayout.LabelField("Output root", EditorStyles.boldLabel);
            EditorGUILayout.BeginHorizontal();
            exportRoot = EditorGUILayout.TextField(exportRoot ?? "");
            if (GUILayout.Button("Browse", GUILayout.Width(78)))
            {
                string selected = EditorUtility.OpenFolderPanel("Choose output directory", exportRoot, "");
                if (!string.IsNullOrEmpty(selected)) exportRoot = selected;
            }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.HelpBox("Original CPK stays untouched. Every run uses a unique output directory and keeps its own log.", MessageType.None);
            EditorGUILayout.BeginHorizontal();
            EditorGUI.BeginDisabledGroup(cliSyntax == 0);
            if (GUILayout.Button("List Archive (legacy only)", GUILayout.Height(30))) BeginOperation(false);
            EditorGUI.EndDisabledGroup();
            if (GUILayout.Button("Extract All", GUILayout.Height(30))) BeginOperation(true);
            EditorGUILayout.EndHorizontal();
            EditorGUI.EndDisabledGroup();

            if (Busy && GUILayout.Button("Stop Extraction", GUILayout.Height(26))) Stop();
            EditorGUILayout.LabelField("Status: " + status, EditorStyles.boldLabel);
            if (!string.IsNullOrEmpty(currentOutput)) EditorGUILayout.SelectableLabel(currentOutput, EditorStyles.textField, GUILayout.Height(18));

            if (!string.IsNullOrEmpty(latestExtract) && Directory.Exists(latestExtract))
            {
                if (GUILayout.Button("Inspect extracted CASM/CANV files"))
                    XBX2UnityMapWindow.OpenAt(latestExtract);
                if (GUILayout.Button("Open extracted folder"))
                    EditorUtility.RevealInFinder(latestExtract);
            }
            EditorGUILayout.Space(3);
            EditorGUILayout.LabelField("Process log", EditorStyles.boldLabel);
            scroll = EditorGUILayout.BeginScrollView(scroll, GUILayout.MinHeight(110));
            EditorGUILayout.TextArea(visibleLog.ToString(), GUILayout.ExpandHeight(true));
            EditorGUILayout.EndScrollView();
            if (GUILayout.Button("Copy log")) EditorGUIUtility.systemCopyBuffer = visibleLog.ToString();

            if (Event.current.type == EventType.Layout)
            {
                EditorPrefs.SetString(Prefix + "cpk", cpkPath ?? "");
                EditorPrefs.SetString(Prefix + "tool", extractorPath ?? "");
                EditorPrefs.SetInt(Prefix + "syntax", cliSyntax);
                EditorPrefs.SetString(Prefix + "export", exportRoot ?? "");
            }
        }

        private void SetCpk(string path)
        {
            if (string.IsNullOrEmpty(path)) return;
            cpkPath = Path.GetFullPath(path);
            status = "Selected: " + Path.GetFileName(cpkPath);
            Repaint();
        }

        private void UseSelection()
        {
            foreach (UnityEngine.Object obj in Selection.objects)
            {
                string path = AssetDatabase.GetAssetPath(obj);
                if (string.IsNullOrEmpty(path) || !string.Equals(Path.GetExtension(path), ".cpk", StringComparison.OrdinalIgnoreCase))
                    continue;
                SetCpk(Path.Combine(ProjectRoot, path));
                return;
            }
            Append("No .cpk asset selected in Unity Project window.");
        }

        private static bool CheckHeader(string path, out string reason)
        {
            reason = "Not a valid CRIWARE CPK header.";
            try
            {
                using (var stream = File.OpenRead(path))
                {
                    if (stream.Length < 4) { reason = "File has fewer than four bytes."; return false; }
                    byte[] data = new byte[4];
                    if (stream.Read(data, 0, data.Length) != 4) return false;
                    if (data[0] != 0x43 || data[1] != 0x50 || data[2] != 0x4B || data[3] != 0x20)
                    {
                        reason = "Expected CPK magic: 43 50 4B 20 (CPK ).";
                        return false;
                    }
                }
                return true;
            }
            catch (Exception ex) { reason = ex.Message; return false; }
        }

        private bool ValidateInput()
        {
            if (Application.platform != RuntimePlatform.WindowsEditor)
            {
                EditorUtility.DisplayDialog("Windows tool required", "This version runs the Windows CriPakTool.exe program.", "OK");
                return false;
            }
            if (string.IsNullOrEmpty(cpkPath) || !File.Exists(cpkPath) ||
                !string.Equals(Path.GetExtension(cpkPath), ".cpk", StringComparison.OrdinalIgnoreCase))
            { EditorUtility.DisplayDialog("Missing CPK", "Choose an existing .cpk file.", "OK"); return false; }
            string reason;
            if (!CheckHeader(cpkPath, out reason))
            { EditorUtility.DisplayDialog("CPK header error", reason, "OK"); return false; }
            if (string.IsNullOrEmpty(extractorPath) || !File.Exists(extractorPath) ||
                !string.Equals(Path.GetExtension(extractorPath), ".exe", StringComparison.OrdinalIgnoreCase) ||
                !Path.GetFileNameWithoutExtension(extractorPath).StartsWith("CriPakTool", StringComparison.OrdinalIgnoreCase))
            {
                EditorUtility.DisplayDialog("CriPakTool missing", "Select a CriPakTool.exe or CriPakTools.exe with the syntax chosen in the dropdown.", "OK");
                return false;
            }
            if (string.IsNullOrWhiteSpace(exportRoot))
            { EditorUtility.DisplayDialog("Output required", "Choose an export root.", "OK"); return false; }
            return true;
        }

        private void BeginOperation(bool extract)
        {
            if (!extract && cliSyntax == 0)
            {
                EditorUtility.DisplayDialog("List not supported", "The selected CRI CPK Tool CLI supports extract_all and replace but has no list command. Use Extract All or switch to a legacy tool.", "OK");
                return;
            }
            if (Busy || !ValidateInput()) return;
            string root = Path.GetFullPath(exportRoot);
            string sourceDir = Path.GetDirectoryName(Path.GetFullPath(cpkPath)).TrimEnd(Path.DirectorySeparatorChar);
            string assets = Path.GetFullPath(Application.dataPath).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (root.Equals(sourceDir, StringComparison.OrdinalIgnoreCase) ||
                root.StartsWith(sourceDir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            { EditorUtility.DisplayDialog("Output overlaps source", "Choose an output folder outside the directory containing the original CPK.", "OK"); return; }
            if (root.StartsWith(assets, StringComparison.OrdinalIgnoreCase) &&
                !EditorUtility.DisplayDialog("Output inside Unity Assets", "Large extracted archives can trigger many Unity imports. Continue?", "Continue", "Cancel"))
                return;
            if (extract && !EditorUtility.DisplayDialog("Extract entire CPK archive?",
                "The archive can expand to many gigabytes. Verify that you have sufficient disk space. Extract using CriPakTool.exe?", "Extract", "Cancel"))
                return;

            string operation = extract ? "extract" : "list";
            string name = Path.GetFileNameWithoutExtension(cpkPath);
            string runDirectory = Path.Combine(root, "CPK", name + "_" + operation + "_" + DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff") + "_" + Guid.NewGuid().ToString("N").Substring(0, 6));
            try
            {
                Directory.CreateDirectory(runDirectory);
                currentOutput = runDirectory;
                visibleLog.Length = 0;
                droppedLines = 0;
                cancelling = false;
                extracting = extract;
                outputWriter = new StreamWriter(Path.Combine(runDirectory, "CriPakTool_log.txt"), false, new UTF8Encoding(false));
                outputWriter.AutoFlush = true;
                string extractedDirectory = Path.Combine(runDirectory, "Contents");
                if (extract && cliSyntax == 0) Directory.CreateDirectory(extractedDirectory);
                string arguments = cliSyntax == 0
                    ? "extract_all -p " + Quote(Path.GetFullPath(cpkPath)) + " -o " + Quote(extractedDirectory)
                    : Quote(Path.GetFullPath(cpkPath)) + (extract ? " ALL" : "");
                var si = new ProcessStartInfo
                {
                    FileName = Path.GetFullPath(extractorPath),
                    Arguments = arguments,
                    WorkingDirectory = runDirectory,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };
                process = new Process { StartInfo = si };
                process.OutputDataReceived += (sender, e) => QueueLine(e.Data, false);
                process.ErrorDataReceived += (sender, e) => QueueLine(e.Data, true);
                Append("Starting " + operation + " on " + cpkPath);
                Append("CLI: " + SyntaxOptions[cliSyntax] + " | Arguments: " + arguments);
                if (!process.Start()) throw new IOException("Cannot start CriPakTool.exe");
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                status = "Running " + operation + "...";
            }
            catch (Exception ex)
            {
                Append("Error: " + ex.Message);
                status = "Failed to start";
                try { if (process != null) process.Dispose(); } catch { }
                process = null;
                if (outputWriter != null) { outputWriter.Dispose(); outputWriter = null; }
            }
            Repaint();
        }

        private static string Quote(string path)
        {
            return "\"" + path.Replace("\"", "") + "\"";
        }

        private void QueueLine(string line, bool error)
        {
            if (line == null) return;
            lock (pendingLock)
            {
                if (pendingLines.Count >= 20000) { droppedLines++; return; }
                pendingLines.Enqueue((error ? "[stderr] " : "") + line);
            }
        }

        private void Drain()
        {
            lock (pendingLock)
            {
                while (pendingLines.Count > 0) Append(pendingLines.Dequeue());
            }
        }

        private void UpdateProcess()
        {
            Drain();
            if (process == null) return;
            try
            {
                if (!process.HasExited) return;
                process.WaitForExit();
                Drain();
                int code = process.ExitCode;
                process.Dispose();
                process = null;
                if (droppedLines > 0) Append("Output backpressure: " + droppedLines + " console lines omitted.");
                if (outputWriter != null) { outputWriter.Dispose(); outputWriter = null; }

                int files = 0;
                if (Directory.Exists(currentOutput))
                {
                    foreach (string file in Directory.EnumerateFiles(currentOutput, "*", SearchOption.AllDirectories))
                        if (!string.Equals(Path.GetFileName(file), "CriPakTool_log.txt", StringComparison.OrdinalIgnoreCase)) files++;
                }
                if (cancelling) status = "Cancelled (partial output kept)";
                else if (code != 0) status = "Failed (exit code " + code + ")";
                else if (extracting && files == 0) status = "No extracted files detected — review log";
                else
                {
                    status = extracting ? ("Finished — " + files + " extracted files") : "Archive listed — see log";
                    if (extracting) latestExtract = cliSyntax == 0 ? Path.Combine(currentOutput, "Contents") : currentOutput;
                }
                Append("Process exit: " + code + " | files: " + files + " | " + status);
            }
            catch (Exception ex)
            {
                status = "Process error";
                Append(ex.Message);
                try { process.Dispose(); } catch { }
                process = null;
                if (outputWriter != null) { outputWriter.Dispose(); outputWriter = null; }
            }
            Repaint();
        }

        private void Stop()
        {
            if (process == null) return;
            cancelling = true;
            try { if (!process.HasExited) process.Kill(); }
            catch (Exception e) { Append("Stop error: " + e.Message); }
        }

        private void Append(string text)
        {
            string line = "[" + DateTime.Now.ToString("HH:mm:ss") + "] " + text;
            visibleLog.AppendLine(line);
            if (visibleLog.Length > 35000) visibleLog.Remove(0, visibleLog.Length - 25000);
            try { if (outputWriter != null) outputWriter.WriteLine(line); }
            catch (Exception e) { UnityEngine.Debug.LogWarning("XBX2Unity: cannot write CPK log: " + e.Message); }
        }
    }
}
