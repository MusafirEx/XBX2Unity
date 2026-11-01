# XBX2Unity

Unity Editor package for inspecting Xenoblade Chronicles X **Wii U** map-file pairs and optionally running the third-party CASM extractor.

## Install

Unity **2022.3+**: Window > Package Manager > + > Add package from git URL:

```
https://github.com/MusafirEx/XBX2Unity.git#feature/unity-map-extractor-package
```

Or import the separately distributed `.unitypackage`.

## Use

1. Extract your own Wii U game dump's map files outside Unity or under `Assets/XBX`.
2. Select the folder in Unity Project view, then open **Tools > XBX2Unity > Map Extractor**.
3. Click **Use Project Selection** and **Write Binary Audit**.
4. Download/build `casmExtract.exe` from [XenoToolset](https://github.com/PredatorCZ/XenoToolset), choose it in the window, then select **Extract Selected CASM**.
5. Inspect extracted assets under `<Unity project>/XBX_Exports`. For mesh import, use a compatible Blender Xenoblade importer and export FBX separately.

The wrapper copies inputs into a timestamped output directory. **It does not modify your original game files.** It does not include the third-party executable. The `-u` and `-b` options are the documented XenoToolset command options.

## Supported

- `.casmhd` / `.casmda`: pair discovery, header audit and **external** CASM extraction.
- `.canvhe` / `.canvda`: pair discovery and header audit only.
- Detects missing and zero-byte files, including the observed `ma9990.casmda` case.
- Recursive folder scan, selected groups, safe output staging, process logs and cancellation.

**Not implemented:** native decoding of CASM or CANV, mesh/rig/material import, CPK extraction, and conversion to Unity Terrain. An empty `.casmda` is diagnosed, not assumed to prove corruption.

## Known limitations

- External extraction requires Windows and a compatible `casmExtract.exe`.
- Results depend on third-party tool version and original data quality; no successful decode is guaranteed.
- The window must remain open during extraction. Closing or recompiling scripts cancels the running extraction.
- The package has not been runtime-compiled in the user's Unity project; check Console after installation.
- Outputs are created outside `Assets` by default to avoid Unity reimporting large raw archives.

## Credits

CASM extraction relies on [PredatorCZ/XenoToolset](https://github.com/PredatorCZ/XenoToolset) (GPL-3.0 external tool; **not bundled**). Blender import alternative: [ScanMountGoat/xenoblade_blender](https://github.com/ScanMountGoat/xenoblade_blender).

Package source code is licensed under MIT (see LICENSE.md). Game assets are not provided.
