# XBX2Unity — Xenoblade Chronicles X (Wii U) archive tools

Unity 2022.3+ Editor package for **CPK archive extraction** and inspection/external extraction of **CASM/CANV** map-file pairs. The package does not include third-party executables or game assets.

## Installation (Unity Package Manager)

Window > Package Manager > + > Add package from git URL:

```
https://github.com/MusafirEx/XBX2Unity.git#feature/unity-map-extractor-package
```

Alternatively import the `.unitypackage` from the GitHub Actions build artifact.


### Native importer endianness fix (v0.5.1)

The first native importer in v0.5.0 incorrectly decoded MXMD metadata as little-endian. The user's Unity Editor.log repeatedly reported `Unsupported MXMD version: 942080000 (expected Wii U 10040)` for `ma9990` models. This is a byte-order error: `00 00 27 38` is `10040` big-endian but `942080000` little-endian. **v0.5.1 fixes the version, pointers, counts, vertex descriptors and triangle index reads to big-endian**.

After upgrading the package, right-click `Assets/XBX/ma9990/terrain/0.camdo` and select **Reimport**. If a later geometry parsing issue appears, copy its exact error and a small sample file for focused binary debugging. Packaging CI does not compile or run Unity Editor.


### Material texture stride fix (v0.6.1)

After testing the map textures in Unity, the Editor.log showed many `CAMDO texture metadata: Invalid CAMDO pointer/range: offset=1056965360` warnings (often beginning with `0x3F000000`) and mostly `Bound extracted base textures: 0` despite successful static meshes. Legacy `MXMDMaterial_V1` entries are **136 bytes** (not 132). A short stride shifted subsequent records four bytes into float data, which was interpreted as a texture offset. v0.6.1 uses the original 136-byte layout and reports exact record addresses on invalid links.

**Reimport one** `Assets/XBX/ma9990/terrain/0.camdo` after updating; validate new counts and actual texture mapping before bulk-reimport. This fixes the known material stride defect; it does **not** fix the separate terrainLOD/3, terrainLOD/4 or Skybox0 geometry variants.

## Edge blending diagnostics and preview controls (v0.6.2)

**Observed Unity log:** `terrain/1.camdo` imported **5,602 mesh draw calls** and bound **525 base material textures**, but reported **0 experimentally blended materials**. This occurs when the blending toggle is disabled (default) or when the second selected texture slot does not resolve.

To test the source terrain mask rather than guessing:

1. Update from `main`, select `Assets/XBX/ma9990/terrain/1.camdo` and open **Material**.
2. Enable **Experimental Edge Blend**. Start with **Mask Source = vBlend Attribute (14)**, **Mask Channel = Red**, **Blend Texture Slot = 1** and **Debug Preview = Raw Mask (grayscale)**.
3. Apply changes, then examine the model. Black/white everywhere means that channel may be absent or nearly constant. Switch to **vBlend RGB**, then inspect Green, Blue or Alpha, or try **Vertex Color RGBA** / **Base Texture RGBA** / **Overlay Texture RGBA** for diagnostic comparisons.
4. Use **Debug Preview = Overlay Texture Only** to verify that the second linked texture is actually an albedo/diffuse layer. A normal or packed data texture is *not* an appropriate overlay.
5. For a valid two-color-layer pair and variable mask, set **Debug Preview = Composite**, then adjust Threshold, Softness and Invert Mask. If the second image needs distinct UVs, choose **Overlay UV = UV1**.
6. Use **Tools > XBX2Unity > Inspect Selected CAMDO Blend Data** (or right-click the CAMDO asset > XBX2Unity > Inspect CAMDO Blend Data). It produces `XBX_Exports/BlendAudits/XBX_ma9990_terrain_1.camdo.blend.txt` with mesh UV3 mask min/max, counts of variable vBlend, vertex color/UV1 presence, and sample material base/overlay names.

**Important:** All these blend modes are experiments to identify original vertex/texture controls. They do **not** reproduce the exact original Xenoblade X terrain shader. Source material slot 1 might be a normal map or packed mask, and many meshes may not carry type-14 vBlend. The source mask and overlay texture must be verified visually before relying on the result.

The generated `XBX2Unity/Map Vertex Blend` shader is a simple unlit two-layer debug shader targeting Built-in and URP. It is not verified in HDRP. No GLTF conversion or editing of CAMDO/CASMT files is required.


## Native map textures & experimental edge blending (v0.6.0)

**No glTF conversion**. The native \`.camdo\` ScriptedImporter now also reads Wii U MXMD v10040 **material texture links** and **external texture mapping records**, then binds the extracted textures located under the same stage's \`textures/\` directory. The optional **Map Vertex Blend** shader uses the source \`vBlend\` attribute (type 14, UNorm8x4) for a two-layer edge-blend preview.

### Exact texture paths from XenoToolset casmExtract

Given \`Assets/XBX/ma9990/terrain/0.camdo\`, texture assets are searched under \`Assets/XBX/ma9990/textures/\`:

- **Cached stage textures**: \`textures/<containerId>/<textureId:D4>.png\` — for example \`textures/0/0001.png\`.
- **Uncached/global textures**: \`textures/<externalTextureId:D4>.png\` — for example \`textures/0001.png\`.
- \`.dds\` and \`.tga\` are accepted only at the **same exact mapped path**, when the extractor used a different texture export format.

The internal mapping comes from the four \`int16\` fields of each external texture record and the material's \`texture_index\` entries. This preserves the extractor's texture container ID and texture ID semantics rather than assigning random textures. See [XenoToolset casmExtract](https://github.com/PredatorCZ/XenoToolset/blob/master/casmExtract/casmExtract.cpp) and [xc3_lib's MXMD legacy parser](https://github.com/ScanMountGoat/xc3_lib/blob/main/xc3_lib/src/mxmd/legacy.rs).

### Unity Inspector

1. Update XBX2Unity from \`main\`.
2. Select \`Assets/XBX/ma9990/terrain/0.camdo\` → Inspector → **Material** tab.
3. Enable **Bind Extracted Textures** and start with **Base Texture Slot = 0**. Click Apply and reimport.
4. Check whether the material uses the expected diffuse PNG. **Material slot order is not verified as the original game's diffuse/normal/overlay ordering.** Change Base Texture Slot to inspect a different linked texture, if necessary.
5. If the material has two confirmed diffuse/albedo layers, enable **Experimental Edge Blend**, choose **Blend Texture Slot** and the **vBlend Channel (R/G/B/A)**. Tune **Threshold** and **Softness** for the visible transition. The shader does not infer which color channel is the original edge weight.

The generated shaders use \`XBX2Unity/Map Vertex Blend\` (an unlit diagnostic two-layer shader) and are included under \`Runtime/Shaders/\` in both UPM and \`.unitypackage\` builds. Unity Built-in and URP are targeted; HDRP shader support has **not** been validated. When experimental blending is off, native material preview uses the active render pipeline's Lit shader (URP Lit, HDRP Lit or Built-in Standard).

### What is and isn't reconstructed

- **Implemented:** indexed preview texture lookup, importer dependency tracking on found texture assets, UV0, source vColor optional tint, separate source vBlend data in Unity Mesh.uv3, 2-layer linear blend with controllable channel/threshold/softness.
- **Not implemented:** exact Xenoblade X shader stages and texture roles, true game terrain edge shading, splat/height mask transforms, multi-UV layer routing, PBR maps, LOD seam correction, automatic world-space placement, full material alpha/transparency.
- **Verification:** GitHub Actions currently tests packaging only. Actual binding and blending on \`ma9990\` require a local Unity reimport. Keep **Experimental Edge Blend** off by default to avoid blending a normal map as if it were an albedo texture.


### Native map CAMDO/CASMT geometry support (v0.5.3)

The user's `terrain/0.camdo` header is **not broken**: it has `Models[0x08] = 0x44`, `VertexData[0x14] = 0`, `ExternalBufferIDs[0x28] = 0xE64` and `ExternalBufferCount[0x2C] = 0xFFFFFFFF`. This is the map-extracted CAMDO layout produced by [PredatorCZ/XenoToolset `casmExtract`](https://github.com/PredatorCZ/XenoToolset/blob/master/casmExtract/casmExtract.cpp). That extractor deliberately uses no internal vertex pointer for map terrain and writes the actual geometry into the adjacent `.casmt` file.

The native importer now detects this stage layout and **reads geometry directly from the adjacent `.casmt`**, without converting to glTF/GLB:

- **Terrain**: `0x2C = FFFFFFFF` selects `MXMDTerrainBufferLookupHeader_V1`: group indices → pair index and side → geometry-offset inside `.casmt`.
- **Objects**: positive `0x2C` selects a flat array of geometry offsets into `.casmt`.
- **Standalone models**: nonzero internal vertex pointer `0x14` continues through the embedded mesh-buffer path.
- Each geometry header contains vertex/index buffer descriptors, attribute descriptors and big-endian Wii U vertex/index data; Unity generates Mesh, Material placeholders and GameObjects.
- Both `.camdo` and `.casmt` must remain alongside each other. External geometry reads are currently limited to **512 MiB per CASMT file** to prevent extreme Editor memory usage; streaming/incremental reads can replace this limit later.
- Textures/shader material mapping, terrain instances, rigs/animations and map placement remain future work.

**Test one small pair first**: update the package from `main`, select `Assets/XBX/ma9990/terrain/0.camdo`, right-click → Reimport and inspect the Mesh sub-assets. There may still be format variations requiring fixes because local raw `.camdo/.casmt` data has not been run through the newly implemented parser, and GitHub Actions packaging is not a Unity import test.

### CAMDO binary header audit (v0.5.2)

If Unity displays `Missing Models/VertexData root pointers`, the source file passed the `MXMD 10040` header check but at least one expected root field is null. Do not force offset guesses.

Select one `Assets/XBX/ma9990/terrain/0.camdo` in the Project view and choose **Assets > XBX2Unity > Audit CAMDO Header** (or **Tools > XBX2Unity > Audit Selected CAMDO Header**). The tool logs an informative report and writes a small text file under `XBX_Exports/CAMDO_Audits/` with the first 64 bytes, the big-endian root fields, the source length, and any adjacent CASMT file size. This is a read-only operation. Share the report for further format-specific decoder work.

The v0.5.2 importer itself also reports both pointer values and a bounded header dump in the Console when they are absent. Do not bulk-reimport all CAMDO files until one terrain model is validated.

## Native CAMDO importer (experimental v0.5.0, no GLTF conversion required)

XBX2Unity now includes **XBXCamdoNativeImporter**, a pure C# Unity 2022.3 `ScriptedImporter` for `.camdo` files from **Wii U Xenoblade Chronicles X** using the `MXMD` `10040` format. It follows the same basic Editor/import asset workflow as [EMD2Unity](https://github.com/MusafirEx/EMD2Unity) and [Bress2Unity](https://github.com/MusafirEx/Bress2Unity).

**Workflow:** select `Assets/XBX/ma9990/terrain/0.camdo`, let Unity import it automatically, then click the asset to preview the generated `GameObject` and mesh sub-assets in Inspector, or drag it into Scene. You do **not** need `xc3_gltf.exe`, glTFast, Blender, GLB or FBX for this workflow.

- Reads `MXMD` version `10040` from CAMDO with checked little-endian metadata offsets/pointers.
- Reads Wii U big-endian Float32 vertex positions and TexCoord attributes.
- Reads normal and tangent bytes, optional vertex color, 16-bit triangle indices, mesh draw/material references.
- Builds `MeshFilter`, `MeshRenderer`, `Mesh`, and placeholder `Material` Unity sub-assets.
- Preserves shared mesh data for repeated vertex/index buffer references; optional X mirror and triangle winding adjustment.
- Companion `.casmt` path is tracked as an asset import dependency where present. **CASMT geometry decoding is implemented for map terrain and objects as of v0.5.3; texture decoding is not implemented**.
- Inspector tabs: **Model** (working static-mesh options), **Rig** (not yet supported), **Animation** (not yet supported), **Material** (placeholder settings only).
- Bounded, validated pointers and file/model/mesh/index counts. Invalid/unsupported content reports a Unity import error rather than inventing geometry.
- Retains the separate **CAMDO Model Viewer** GLB-based tool from v0.4.0 as an optional fallback for unsupported native import cases.

### Coverage limitations

**This is an initial native geometry decoder, not a verified complete importer.** It has not yet been compiled/tested against the user's `ma9990` file bytes on a connected Unity Editor. Complex material shader/texture binding, game-specific instance transforms, skeletal skinning, animations, compressed variants and all non-triangle primitives need more work. Some models may appear gray, mirrored or invisible until these details are validated.

Implementation is an independent C# adaptation of public format descriptions in the MIT-licensed [xc3_lib](https://github.com/ScanMountGoat/xc3_lib) (particularly `xc3_lib/src/mxmd/legacy.rs` and `xc3_model/src/vertex.rs`). The repository includes no copied third-party binaries or copyrighted game assets.

## Optional GLB conversion for Wii U CAMDO models (v0.4.0)

This legacy/fallback workflow **does not require Blender** but is optional; for direct native import, see the v0.5.0 section above. It uses the external [xc3_gltf](https://github.com/ScanMountGoat/xc3_lib/tree/main/xc3_gltf) program to convert Wii U `.camdo` models into a GLB, then lets [Unity glTFast](https://docs.unity3d.com/Packages/com.unity.cloud.gltfast@latest) import it into the Project as a previewable, placeable model. This is a conversion workflow rather than a native binary CAMDO `ScriptedImporter`.

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
