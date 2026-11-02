# XBX2Unity — Xenoblade Chronicles X (Wii U) archive tools

Unity 2022.3+ Editor package for **CPK archive extraction** and inspection/external extraction of **CASM/CANV** map-file pairs. The package does not include third-party executables or game assets.

## Installation (Unity Package Manager)

Window > Package Manager > + > Add package from git URL:

```
https://github.com/MusafirEx/XBX2Unity.git#feature/unity-map-extractor-package
```

Alternatively import the `.unitypackage` from the GitHub Actions build artifact.

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

- There is **no native CPK/CASM/CANV mesh or texture decoder**. Output of CPK extraction is still game data.
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
