# XBX2Unity — Xenoblade Chronicles X (Wii U) archive tools

Unity 2022.3+ Editor package for **CPK archive extraction** and inspection/external extraction of **CASM/CANV** map-file pairs. The package does not include third-party executables or game assets.

## Installation (Unity Package Manager)

Window > Package Manager > + > Add package from git URL:

```
https://github.com/MusafirEx/XBX2Unity.git#feature/unity-map-extractor-package
```

Alternatively import the `.unitypackage` from the GitHub Actions build artifact.


## Preview Wii U CAMDO models directly in Unity (v0.4.0)

This workflow **does not require Blender**. It uses the external [xc3_gltf](https://github.com/ScanMountGoat/xc3_lib/tree/main/xc3_gltf) program to convert Wii U `.camdo` models into a GLB, then lets [Unity glTFast](https://docs.unity3d.com/Packages/com.unity.cloud.gltfast@latest) import it into the Project as a previewable, placeable model. This is a conversion workflow rather than a native binary CAMDO `ScriptedImporter`.

1. Download the Windows `xc3_lib_win_x64.zip` from [xc3_lib releases](https://github.com/ScanMountGoat/xc3_lib/releases). Extract it and locate **xc3_gltf.exe**. Do not choose `casmExtract.exe` or `XenobladeToolset.exe`.
2. In Unity Package Manager, install **com.unity.cloud.gltfast** if it is not installed. The RTS Unity project already had it installed at version 6.14.1 in the previously inspected manifest.
3. Extract your `map.cpk` using XBX2Unity CPK Extractor, then extract CASM via XenoToolset to generate files like:
   ```
   Assets/XBX/ma9990/terrain/0.camdo
   Assets/XBX/ma9990/terrain/0.casmt
   Assets/XBX/ma9990/objects/...
   Assets/XBX/ma9990/Skybox0.camdo
   ```
4. Select a `.camdo` asset in Unity's Project view, choose **Assets > XBX2Unity > Convert and View CAMDO** (or open **Tools > XBX2Unity > CAMDO Model Viewer**).
5. Browse to `xc3_gltf.exe` and click **Convert Selected & View**. The plugin executes:
   ```powershell
   xc3_gltf.exe "C:\\path\\to\\0.camdo" --output "C:\\project\\XBX_Exports\\ModelConversion\\<unique>\\0.glb"
   ```
   The `.casmt` companion and internal model data are resolved by the converter based on the CAMDO location.
6. Upon successful conversion, the tool copies the validated GLB to `Assets/XBX_Imported/XBX/ma9990/terrain/0.glb`. glTFast creates a model asset; the plugin selects/pings it and, if enabled, instantiates it into the active Scene and frames the Scene view.

Selecting a folder such as `Assets/XBX/ma9990/terrain` or `Assets/XBX/ma9990` also works. **Convert Up To 20 Models** converts a bounded batch sequentially to avoid accidentally creating thousands of heavy model assets. The single-file mode is recommended for the first test.

### Model viewing status and limitations

- GLB contains geometry and attributes supported by the **xc3_gltf** converter: vertices, normals, tangents, UVs, vertex colors, skin weights, and available textures/materials. The original game shader appearance may differ.
- The converter automatically finds the paired `.casmt` where applicable. Some `.camdo` files (such as Skybox or LOD assets) may not have one, and some require additional source data or specific converter versions.
- The extracted `textures/` tree is not automatically mapped by folder number in XBX2Unity. Conversion uses the source converter's own dependency rules.
- glTFast's generated `.glb` asset is viewable and can be dragged into a Scene or opened in Prefab/Inspector preview. No direct custom `.camdo` inspector has been implemented yet.
- This release has not been verified with a real `ma9990` binary via a connected Windows Unity Editor. GitHub packaging CI is **not** an actual model conversion or Unity compilation test.
- Converter outputs and logs are written under `XBX_Exports/ModelConversion/`, outside Assets; successful GLBs only go under `Assets/XBX_Imported`.
- The converter executable and game assets are **not distributed** by XBX2Unity. Respect third-party software licenses and game asset copyright.


## Starting with `map.cpk`

1. Select `Assets/XBX/map.cpk` in Unity's Project window.
2. Open **Assets > XBX2Unity > Extract Selected CPK**, or **Tools > XBX2Unity > CPK Extractor**.
3. Select your `CriPakTool.exe` or `CriPakTools.exe` and choose the matching **Tool command syntax**:
   - **CRI CPK Tool (default, supported by the supplied logs):** `CriPakTools.exe extract_all -p "archive.cpk" -o "output_folder"`. Supports extraction but **not** the List Archive button.
   - **Legacy CriPakTool:** `CriPakTool.exe "archive.cpk" ALL`. Can also list archive contents via `CriPakTool.exe "archive.cpk"` on compatible builds.
4. Click **Extract All**. The tool asks for confirmation as the archive may expand to many gigabytes.
5. Output goes to `<Unity Project>/XBX_Exports/CPK/<archive>_extract_<timestamp>/Contents/` for the default CRI CPK Tool, or into the run directory for the legacy tool. No copying of the original CPK is required. Results and process logs are isolated per run.
6. Click **Inspect extracted CASM/CANV files** in the CPK window. This launches **Tools > XBX2Unity > Map Extractor** on the extracted folder.

For `.casmhd` + `.casmda` extraction, select `casmExtract.exe` from [XenoToolset](https://github.com/PredatorCZ/XenoToolset) in the Map Extractor window. This is a separate program. For Blender/FBX import, see [xenoblade_blender](https://github.com/ScanMountGoat/xenoblade_blender).

## Supported features

- `.cpk`: selected-file handling, CRIWARE magic header check (`CPK `), **two CLI syntax modes**, external extraction; legacy-only list option, process logs, Stop, output separation.
- `.casmhd`/`.casmda`: group detection, 64-byte header audit, missing/zero-byte warnings and external `casmExtract.exe` integration.
- `.canvhe`/`.canvda`: pair detection and binary header audit only.
- Reads files at their original location; CPK extraction does not duplicate large source archives in temporary folders.
- Uses Windows Editor-only process execution; no runtime code or compiled native DLLs.

### Important limitations

- There is **no native CPK/CASM/CANV mesh or texture decoder**. To view `.camdo` models, use the external xc3_gltf GLB conversion described above.
- Match the **Tool command syntax** to the executable. The newer tool used in the user's log shows `extract_all -p <cpk_file> -o <output_dir>`, whereas earlier legacy builds accept `<cpk_file> ALL`. The newer tool does not advertise listing.
- Some CriPakTool versions may fail on archives larger than 2 GiB or other unsupported CPK variations; check the process log.
- Game archives may contain arbitrary embedded paths; only extract CPK files from trusted personal dumps. Inspect archive contents first. The external tool performs the actual filesystem writes.
- `ma9990.casmda` can be zero bytes in a particular dump; the tool reports this without assuming the reason.
- Closing an extractor window or triggering a script recompile stops its active external process; incomplete output is retained.
- Unity Editor compilation and game-data extraction require local testing; the CI package build tests packaging, **not** correct decoding.

## Build distributables

Python 3.11+:

```bash
python tools/build_unitypackage.py
```

This generates `dist/XBX2Unity-UPM-vVERSION.zip` and `dist/XBX2Unity-vVERSION.unitypackage`. GitHub Actions uploads both in the `XBX2Unity-packages` artifact.

## Credits and license

- [CriPakTools](https://github.com/esperknight/CriPakTools) for CRIWARE archive listing and extraction (external tool; not bundled).
- [XenoToolset](https://github.com/PredatorCZ/XenoToolset) for CASM map extraction (external tool; not bundled).
- XBX2Unity package source: MIT, see [LICENSE.md](LICENSE.md). Proprietary game content is not included.
