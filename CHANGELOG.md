# Changelog

## 0.5.1 - 2026-11-02
- Fix repeated Wii U CAMDO native importer failure: `Unsupported MXMD version: 942080000`.
- Decode all MXMD v10040 integer metadata (header version, pointers, counts, vertex descriptors and u16 indices) as big-endian, matching xc3_lib's Wii U legacy reader.
- Preserve big-endian vertex floats, existing mesh import safety checks and placeholder-material behavior.
- Importer needs Unity Editor reimport after upgrading; real mesh accuracy still requires verification.


## 0.5.0 - 2026-11-02
- Add experimental pure-C# CAMDO MXMD 10040 native `ScriptedImporter` with Unity mesh sub-assets.
- Decode static mesh vertex/index references, positions, normals, tangents, UV and vertex colors; create placeholder materials.
- Add native importer Inspector with Model, Rig, Animation and Material tabs, accurately identifying unsupported features.
- Preserve the optional external xc3_gltf model viewer as fallback; no glTFast dependency required for native importer.
- Add native importer to GitHub Actions UPM ZIP and `.unitypackage` builds.


## 0.4.0 - 2026-11-02
- Add CAMDO Model Viewer editor window with single-file and bounded batch conversion to GLB.
- Use external `xc3_gltf.exe` converter (from ScanMountGoat/xc3_lib) to read extracted Wii U CAMDO/CASMT model pairs.
- Import generated GLB as a Unity glTFast model asset, optionally instantiate and frame it in the active Scene.
- Validate GLB v2 header, avoid touching source files, keep logs, and support cancellation.
- Include the new Editor tool and metadata in both UPM and UnityPackage builds.
- Explicitly document conversion limitations and required external dependencies.


## 0.3.2 - 2026-11-02
- Correct CPK extraction command for CRI CPK Tool: `extract_all -p "archive.cpk" -o "output_dir"`.
- Add explicit syntax selector; retain legacy `"archive.cpk" ALL` support.
- Disable List Archive for the modern variant, which advertises no listing operation.
- Keep extracted files under `Contents/` and log the exact argument pattern.


## 0.3.1 - 2026-11-02
- Fix Unity magic-method warning by renaming `Start(bool)` to `BeginOperation(bool)` in the CPK editor window.
- Provide explicit Unity `.meta` files for UPM documentation and tools to avoid immutable-package warnings.


## 0.3.0 - 2026-11-02
- Add selected `.cpk` archive support in the Unity Editor.
- Invoke external CriPakTool to list/extract archives directly from selected Project assets.
- Add CPK header validation, 2 GiB warning, unique extraction folders, process log, Stop, and CASM/CANV handoff.
- Include CPK tool in UnityPackage and UPM ZIP build.


## 0.2.0 - 2026-11-02
- Initialize XBX2Unity as a Unity Package Manager package.
- Add safe, editor-only CASM/CANV file discovery and binary audit.
- Add external casmExtract process wrapper with PNG and BC5 options, result logging and cancellation.
- Add documented limitations and game-data safeguards.
