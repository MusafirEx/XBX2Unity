# XBX2Unity — Xenoblade Chronicles X (Wii U) archive tools

Unity 2022.3+ Editor package for **CPK archive extraction** and inspection/external extraction of **CASM/CANV** map-file pairs. The package does not include third-party executables or game assets.

## Installation (Unity Package Manager)

Window > Package Manager > + > Add package from git URL:

```
https://github.com/MusafirEx/XBX2Unity.git
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


## Skell Animation Phase 1 - Wii U hkc SAR sources (v0.10.0)

For the **original Wii U Xenoblade Chronicles X**, Skell combat animations are typically stored under `hkc.cpk/dl/` as per-Skell `*.sar` archives (e.g. `dl019600.sar`); their entries include Havok `*.hkt` motions (idle, run, jump, arts). The `*_rig.hkt` beside a `*.camdo` is a separate **skeleton** and must not be imported as if it were an animation.

1. Extract a corresponding Skell `*.sar` from your own original `hkc.cpk` into a folder you control; the CAMDO and Skell rig remain as they were.
2. Select the CAMDO in Unity and open **Animation > Audit Original Skell .sar Archive** (or **Tools > XBX2Unity > Animation > Audit Skell SAR**). Choose the actual `*.sar` file. The report is written under `XBX_Exports/AnimationAudits/<archive>_sar_audit.txt`.
3. Use **Extract Raw Animation HKT Entries from .sar** to copy the source entries to an output folder you choose. Names are sanitized and prefixed with entry IDs; existing exports are not overwritten. This is raw binary extraction, **not** clip decoding.

The audit supports a validated `1RAS` table variant; another header, pointer convention or HKT codec will be reported as unsupported until tested against an authentic sample. It identifies potential `hkaSplineCompressedAnimation`, `hkaDeltaCompressedAnimation`, `hkaInterleavedUncompressedAnimation` and other Havok class-name hints when present. This is *metadata*, not decoded keyframes. Source `*.hkt` still needs format-specific motion decoding, duration sampling and bone mapping before `UnityEngine.AnimationClip` import can be implemented. No placeholder motion clips are emitted.

**Next validation:** inspect the `dl019600.sar` audit and one extracted `*_anm.hkt` binary, compare its class/track layout with the imported Skell skeleton, then implement the first real animation clip decoder. The existing HKT rig, skinned mesh and material mappings are deliberately unchanged.

## Verified-in-game Skell companion roles for dl099110 (v0.9.3; linear-mask update v0.9.4)

Direct visual comparison with the original game on `dl099110` established:
- **Green (G)** channel of source `*_RGB` = Skell **body part** mask.
- **Blue (B)** channel of source `*_RGB` = Skell **glowing part** mask.
- **Red (R)** remains **unknown**, no color rule applied.
- Audit sampled BC1 alpha was consistently opaque; no verified alpha paint role.

The previous v0.9.1/0.9.2 experimental notion of four independent RGBA paint zones is now deprecated for `dl099110`. This observation is **specific to the tested Skell**, not a validated rule for every model or a full recreation of the original game shader.

### Material > Skell Layer Preview Shader

The new controls are **independent and opt-in**:
- **Green -> Body Color Tint** (OFF). Set **Skell Body Color**, Body Strength (0–1) and Body Mask Gain (0.1–8) to recolor the mesh where companion Green is present. Default color is white, which leaves the existing base colors unchanged.
- **Blue -> Glow Emission Preview** (OFF). Set **Skell Glow Color** (HDR capable), Glow Intensity (0–8) and Blue Mask Gain (0.1–8) to visualize where companion Blue emits light. This is additive color in an unlit preview shader, NOT automatically a real scene light or bloom; URP/Built-in postprocessing/HDR are separately required for proper glow.
- **Layer Debug View** adds **Observed Green – Body Mask**, **Observed Blue – Glow Mask**, and **Observed Body/Glow – Green and Blue Overlay**. They show the unmodified source, even when effect toggles are OFF.
- The **Legacy Unverified RGBA Experiment** is retained for comparison but is bypassed whenever observed Green/Blue role mapping is enabled. Do not use it to claim four confirmed paint zones.

External `.mat` override assets are **never altered** by import settings. To test generated Layer Preview controls, temporarily clear the relevant material's external override and Apply/Reimport. To use the new effects on an existing editable external `.mat`, manually choose `XBX2Unity/Skell Layer Preview` as that material's shader, assign original source `*_RGB` to `_PaintMask`, and configure the material's exposed G/B channel controls; no automatic mutation of previously extracted material files occurs.

**Test:** `dl099110.camdo` > Material > Import Skell MTXT Textures On > Use Skell Layer Preview Shader On (keep other explicit shader override and old diagnostic shader OFF) > select observed G/B debug views > enable Green Body and adjust body tint > enable Blue Glow and adjust intensity. Report mismatch if G affects non-body areas or B illuminates non-glow regions. Original Skell shader and untested models may have different semantics.

**Verification limit:** CI checks source contracts and package creation, not compilation or visual fidelity in Unity 2022.3.


## Skell RGB companion channel audit (v0.9.2)

The user's Unity log for `dl099110.camdo` reports 28 MTXT textures, 60 vertex-color buffers and 6 source material links to `*_RGB` companions. **This does not establish which mask is used by Skell customization.** Format 49 (BC1) provides RGB but only 1-bit alpha (0 or 255), so assuming four independent RGBA paint gradients is unfounded.

The new **XBXSkellPaintChannelAudit** samples each deswizzled BC1 base mip *before* `Texture2D.Apply(false,true)` drops its readable pixel data. It records per-channel R/G/B/A min, sampled mean, max, near-black and near-white fractions and a simple chromatic-fraction indicator. Data are saved in the **XBX Material Manifest**, next to the correct source material/slot/texture ID; masks are not modified.

In **Material > Source MXMD Materials / Unity Remapping**, each `*_RGB` source link shows its BC1 sample summary. Use **Export RGB Companion Channel Audit (.txt)** to save an independent report to the chosen location.

In **Skell Layer Preview Shader > Layer Debug View**, compare:
- RGB Companion Color (unmodified)
- Selected RGB Channel Grayscale (R/G/B/A)
- Selected Channel Binary Threshold (50%)
- Original Mesh Vertex Colors
- Normalized RGB Channel Dominance
- Base versus RGB Companion Difference
- BC1 Companion Alpha (binary)

Unlike v0.9.1, a companion texture is now bound to the **generated diagnostic preview** whenever present even when **Interpret _RGB as Paint Mask** is OFF. This source inspection does not recolor the mesh. Tint/mask application remains opt-in with default strength 0 and white colors; its RGBA-four-zone idea is still unproven. Previously extracted external `.mat` overrides always take precedence and are never modified.

To test `dl099110.camdo`, enable Import Skell MTXT Textures and Skell Layer Preview, leave Interpret _RGB as Paint Mask OFF, and Apply/Reimport. Use mask channel R, G, B and Alpha visualizations before comparing any actual game paint color. For the generated preview to show, temporarily clear the external `.mat` override for that source material; restore it after checking.

This is a diagnostic step, **not** the final source shader or an animation importer. GitHub CI tests source contracts and BC1 sample goldens, not Unity HLSL or C# compilation.


## Skell source-linked Normal, Specular and Paint layers (v0.9.1)

v0.9.0's external `.mat` material overrides were confirmed persistent by the user. v0.9.1 builds the next **material phase**, not animations, while maintaining this exact override workflow.

From live `dl019100`, `dl019600` and `dl109100` MXMD import logs, source name and compressed texture format hints are mapped **within each material's original texture index list**:
- `*_NRM`, `*_NRM_B`, `*_NRMC`: **BC5** normal candidate. Source BC5 may require platform-specific tangent-space/green-channel conventions.
- `*_SPM`: **BC1** specular candidate.
- `*_RFM`, `*_REF`, `*_RF`: reflection candidate. Reported in manifest, never automatically bound to specular/albedo.
- `*_GLO`, `*_GLW`: glow candidate. Reported only.
- `*_RGB`: companion color/paint candidate. Its actual role, channel packing and Skell four-zone mask semantics **have not been established**; never assume source RGB is a native four-zone mask.

### New Material Inspector controls

- **Bind BC5 Normal Candidates** (OFF): opt-in binds decoded BC5 source to generated material `_BumpMap`/`_NormalMap` if shader exposes them, enabling Unity `_NORMALMAP`. Not guaranteed identical to original Skell normal rendering.
- **Bind SPM Specular Candidates** (OFF): opt-in uses a matching BC1 `*_SPM` only if the generated Unity shader has a `_SpecGlossMap` property. URP workflow flags are configured for specular if available; other shaders may ignore this texture.
- **Use Skell Layer Preview Shader** (OFF): creates a generated `XBX2Unity/Skell Layer Preview` material. The viewport has **Composite**, **Raw Base Color**, **BC5 Normal Candidate**, **SPM Specular Candidate**, and **RGBA RGB Companion Candidate** debug modes. The shader uses only an approximate fixed-light detail preview, not the original game's shading.
- **Interpret _RGB as Paint Mask (Unverified)** (OFF): if intentionally enabled with the Layer Preview shader, treats RGB companion RGBA as four zones with independent four paint colors. Its paint strength defaults to **0** even when enabled; this is a controlled experiment, **not a reconstruction** of Skell Customize Colors.
- **Source MXMD Materials / Unity Remapping** now displays original candidate slots for Normal, Specular, Reflection, Glow and RGB Companion, and each MTXT source link's inferred role.

### Existing extracted .mat files are left untouched

These new generated-material settings **never mutate external `.mat` assets**: v0.9.0's persistent per-MXMD external overrides always take precedence. To see new source maps in the Scene, temporarily clear the external .mat override for that particular source slot, Apply/Reimport, and inspect its generated material. You can then copy the discovered source textures and settings into your existing editable `.mat` yourself. Do not overwrite previously edited materials during extraction.

To test `dl019100.camdo`: enable Import Skell MTXT Textures, leave existing base texture setting, enable Bind BC5 Normal Candidates and Bind SPM Specular Candidates, then enable Use Skell Layer Preview Shader **only if** no explicit generated shader/older diagnostic shader has higher priority. Click Apply/Reimport. Compare the normal, SPM and RGB diagnostic views before attempting any paint recoloring. Disable the paint-mask checkbox for baseline comparisons.

**Validation limits:** GitHub CI tests role-matching contracts, package content and earlier GX2 regressions; the user should verify Unity 2022.3 C# compilation, shader compatibility and visual quality in a local project. HKT rigging, bone weights, texture deswizzle and source-model files are unchanged.


## XBX-native Unity Material workflow (v0.9.0)

This release follows the **material editing workflow** of [EMD2Unity](https://github.com/MusafirEx/EMD2Unity), [Unity-CnC-SAGE-Tools W3X/W3D](https://github.com/MusafirEx/Unity-CnC-SAGE-Tools), and [Bress2Unity](https://github.com/MusafirEx/Bress2Unity) while retaining Xenoblade X source semantics.

**What is shared with the other importers:**
- Source-indexed material manifest saved as a `XBX Material Manifest` ScriptableObject sub-asset, with index/preview material/source textures/renderer counts.
- Material Inspector lists all MXMD material slots and source MTXT texture names, format, usage and binding status.
- Optional **Generated Material Shader** override (Unity `Shader` reference). Without it, the importer chooses a Lit shader appropriate for the active Built-in/URP/HDRP render pipeline. Explicit shaders override the diagnostic and terrain fallback shader.
- **External .mat Override** on each source MXMD index; choose your own editable Material asset under `Assets/`. External materials cannot point at their own CAMDO imported sub-assets and are never written back to the source file.
- **Extract** per material or **Extract All Generated Materials as .mat and Assign Overrides**: copies generated materials to `Assets/<source-folder>/<CAMDO-name>_Materials/MXMD_0000.mat` etc. Existing valid external overrides are preserved. Apply / SaveAndReimport relinks those external .mat assets to the original material indices.

**What remains specific to XBX:** The original MXMD numeric material indices and slot pointers, big-endian GX2 MTXT/BC1/BC5 decoder, exact source texture IDs, gMatCol, optional _RGB companion preference and custom Skell paint diagnostics. Native HKT bones, skin weights, static mesh fallback and UV handling are not changed. The source game's original shader stages are still not fully reconstructed and no W3X FXShader, EMD EMAT or BRRES TEV is applied to an XBX model.

**How to use:** Update package and reimport `dl109100.camdo` or `dl019600.camdo`. In **Material**, enable Skell textures as before, scroll to **Source MXMD Materials / Unity Remapping**, inspect generated material / source texture slots. Click **Extract** or **Extract All**, edit the resulting `.mat` file(s) in the Project, and Apply/Reimport to see them on the XBX mesh. Assign any other external `.mat` using the override field if desired. To return to native preview, clear the override, then Apply.

**Verification limits:** v0.9.0 was designed from the live source repositories and CI checks source/packaging. Unity 2022.3 importer compilation and runtime preview must still be verified locally, especially the new Material Inspector and external override persistence.


## Skell source texture selection and _RGB companion (v0.8.4)

The Unity `dl109100.camdo` Editor.log from November 2, 2026 reports **24 MTXT textures decoded, 7 material textures bound, 38 draw calls**, with the first five applicable material groups automatically picking `*_RGB` texture variants because v0.8.3 assigned them a score of 160 vs 90 for unsuffixed base textures. Source entries include `DL109100_FL` alongside `DL109100_FL_RGB`, `DMG`/ `DMG_RGB`, `DL109100_CP`/`_RGB`, `DL109100_SKY`/`_RGB`, and `DL109100`/`_RGB`.

**Source semantics are not confirmed.** Since Skell has customizable paint, `*_RGB` may be a specialized paint or composite input instead of the surface's default base color. We no longer force these textures as albedo.

**New option:** select `dl109100.camdo`, Inspector **Material > Import Skell MTXT Textures > Auto Select Skell Color Texture**. Leave **Prefer Skell _RGB as Albedo** **OFF** (new default) and Reimport. This prefers the primary unsuffixed BC1 texture (e.g. `DL109100_FL`) when available, without changing the original source texture index table. Turn it ON only to compare the `*_RGB` companion. For manual comparison disable Auto Select and choose Base Texture Slot 0 or 1. BC5 normal maps remain excluded from base color.

V0.8.4 also excludes role tokens `_RFM_B`, `_NRM_B`, `_GLW_B` and similar variant-suffixed data maps, which were previously sometimes misclassified. The original Wii U 2D texture decode and working HKT/SkinnedMeshRenderer are unchanged.

**Verification:** The CI source heuristic regression checks all seven `dl109100` logged material groups in both modes. It does not reproduce original game Skell paint or prove which image is visually correct. Confirm in Unity with Raw Color Texture after Reimport and inspect the Console's `Skell material X rgbPreference=... selected slot=...` diagnostics.


## Skell diagnostic shader syntax fix (v0.8.3)

Unity 2022.3 D3D11 `Editor.log` reported:
`Shader error in 'XBX2Unity/Skell Color Diagnostic': syntax error: unexpected token 'line'`
at `XBXSkellColorDiagnostic.shader` lines 56, 58, 59. The HLSL reserved identifier `line` was accidentally used as a local variable for checkerboard grid lines. v0.8.3 renames it to `gridLineWeight`, retaining UV0 Checkerboard/Gradient and all other shader output unchanged. The Editor C# assembly had compiled without errors.

After updating the Git UPM package and recompiling shaders, reselect `dl019600.camdo`, open **Material > Skell Color Diagnostic Shader**, select **UV0 Checkerboard (no texture)** and confirm no shader syntax errors, then compare to **Raw Color Texture**. Note that GitHub Actions only runs source assertions and builds package archives; a Windows Unity D3D11 shader compile still needs checking locally.

## Skell MTXT deswizzle and UV test fix (v0.8.2)

The user confirmed that **Raw Color Texture itself maps incorrectly** on Skell models. This diagnostic excludes gMatCol and custom paint tint; the problem must be investigated at texture block decoding, source-material selection or UV mapping.

**Verified decoder defect:** `wiiu_swizzle/src/addrlib.rs` computes Wii U 2-pipe routing as `pipe_bit0 = y3 XOR x3`, but XBX2Unity v0.8.1 used `y3 XOR x4`. This shifted BC1/BC5 compressed blocks into incorrect positions even with valid source UVs. v0.8.2 fixes it and adds a Python regression test of reference address goldens and block uniqueness, run in GitHub CI before package creation.

The imported material source links also show `DL019600_FL_SPM` with usage **32**, which previously passed the generic color test. Automatic base color now refuses source names ending in `_SPM`, `_RFM`, `_RF`, `_REF`, `_GLO`, `_GLW`, `_OCL`, or `_DEP`, regardless of generic usage. Explicit `_RGB`, `_COL`, `_DCL`, `_AVA` get higher score than ambiguous bare names. Manual texture index mode remains available for precise comparisons.

**Unity test**: update to v0.8.2; select `dl019600.camdo` > Material, enable `Import Skell MTXT Textures`, `Auto Select Skell Color Texture`, and `Skell Color Diagnostic Shader`. Reimport. First select `Raw Color Texture`. Then try `UV0 Checkerboard (no texture)` and `UV0 Gradient (no texture)`. If checker coordinates align well with the mesh but the raw image is scrambled, inspect GX2 texture decoding. If the checker itself tears or stretches strangely, investigate source UV0 attribute coordinates and `Flip V on CAMDO UVs` (same option as Model > Flip UV V). You can compare Auto Select to manual Base Texture Slot 0/1 when source material lists explicit RGB links.

**Bone/skinning unchanged**: source rig, HKT hierarchy, MXMD weight remap and `SkinnedMeshRenderer` remain the proven v0.7.2 behavior. Shader customization/animations remain later phases. CI validates package contents/address math, not real visual texture mapping in Unity.

## Skell original color / material diagnostics (v0.8.1)

Unity Editor.log confirmed dl019600.camdo imports 20 MTXT texture assets and 5 mapped Skell material images, but the model has incorrect surface colors. Skinning works on 31/31 mesh draw calls.

**Probable original issue:** v0.8.0 always used material texture slot 0 as base color, even when the source image was a BC5 normal map, and forced Unity material colors to white instead of reading MXMD gMatCol.

Changes in Material Inspector:
- Auto Select Skell Color Texture (ON by default): inspect all material links, favor BC1 textures with original COL/RGB/AVA usage hints; skip BC5 and NRM.
- Use Skell Material Color (ON by default): apply MXMD legacy Material.color as the source gMatCol RGBA. Disable to compare without source tint.
- Skell Color Diagnostic Shader (OFF by default): opt-in unlit XBX2Unity/Skell Color Diagnostic shader. Debug View = Composite, Raw Color Texture, Source Vertex Color, or Source gMatCol.
- Skell Use Vertex Color (OFF by default): multiply source vColor when it genuinely represents tint. Do not automatically interpret all source vColor as visible color.
- Base Texture Slot stays available in manual selection mode, but BC5/NRM is still rejected as albedo.

After selecting dl019600.camdo, reimport and read Console: up to 24 source material diagnostic lines show each link's source ID, BC1/BC5 format, original usage code, source filename, score, chosen slot and source material color.

**Important:** this is not a reconstruction of the original Monolith Soft Skell shader. Source color can also depend on multi-layer painting, specular/metallic reflections, masks or other parameters. If the Raw Color Texture looks corrupt or tiled incorrectly, investigate GX2 swizzle/mapping before changing shader tint. Skinning and animation are untouched.

## Skell native MTXT texture preview (v0.8.0)

The user tested Skell bone deformation on two models and confirmed the v0.7.2 hierarchy, weight remapping and SkinnedMeshRenderer work. The new work begins the **Texture phase** while preserving rig and weight behavior.

**Experimental and opt-in:** select a `.camdo` model such as `dl019100.camdo`, open Inspector **Material** tab, check **Import Skell MTXT Textures**, keep **Base Texture Slot = 0** initially, and click Apply/Reimport. The importer:
- Parses Wii U big-endian **MXMD packed textures** at root offset `0x1C` in the CAMDO.
- Parses optional **MXMD Streaming** at `0x24`, including low/high CASMT texture descriptor tables, material texture indices, and high-res-to-low-res mapping. High-res texture replaces low-res when present.
- Reads **MTXT** GX2 surface footer from each referenced texture payload (112 bytes) and uses its format, dimensions, pitch, swizzle, and tile mode.
- Supports **BC1 (format 49)** and **BC5 (format 53)** first mip only, **GX2 tile 2/4**. Reorders compressed 4x4 blocks with Wii U 2-pipe/4-bank 256-byte-interleave address math and creates Unity `TextureFormat.DXT1` or `TextureFormat.BC5` subassets.
- Binds generated textures into each material via its **MXMD texture index** at the chosen base material slot, rather than filename guessing. The Inspector `Max Skell Textures` setting defaults to 96 and bounds memory use.
- Leaves unsupported formats (BC2/3/4, RGBA8, unusual tiling, multi-layer, cube and mip tails) unbound. The original game's shader stage/semantic role of each material slot is not reconstructed.

The imported root will contain `mtxt_0000`, etc., Texture2D subassets. The Unity Console logs `Skell Decoded MTXT base textures=N` and `native Skell material textures=N`. **These numbers do not prove that color/normal material roles are correct.** If base slot 0 is BC5 normal data, inspect another texture slot; do not use BC5 as a color/albedo layer. The decoder preserves existing imported Mesh, SkinnedMeshRenderer and bone hierarchy if texture parsing fails.

The user's existing Skell resource audit found **15 MTXT footer candidates in CAMDO and 30 in CASMT** for `dl019100` (BC1/BC5 tile modes 2/4). This release now uses **referenced** MTXT records rather than scanning for image bytes and assigning by order. The results still need to be tested in Unity; GitHub CI only packages scripts.

References: [xc3_lib MTXT](https://github.com/ScanMountGoat/xc3_lib/blob/main/xc3_lib/src/mtxt.rs), [legacy MXMD](https://github.com/ScanMountGoat/xc3_lib/blob/main/xc3_lib/src/mxmd/legacy.rs), [msrd streaming](https://github.com/ScanMountGoat/xc3_lib/blob/main/xc3_lib/src/msrd.rs), [wiiu_swizzle addrlib](https://github.com/ScanMountGoat/wiiu_swizzle/blob/main/src/addrlib.rs).

## Skell weight buffer selector fix (v0.7.2)

Actual Unity `Editor.log` after enabling **Rig > Import Skell Bone Weights** on v0.7.1 showed:

| Skell model | Skinned draws | Static fallback draws | Reason |
| --- | ---: | ---: | --- |
| dl019100 | 1 | 53 | `weight header slot 4=0 has no validated shared skin buffer` |
| dl019600 | 2 | 29 | Same |

v0.7.1 rejected a valid legacy fallback selector. In the [xc3_model implementation](https://github.com/ScanMountGoat/xc3_lib/blob/main/xc3_model/src/vertex.rs), the six header indices reference **original vertex buffers** and are mapped into the separate weight-buffer collection using `index.checked_sub(first_weight_buffer)`. When that subtraction fails, the [legacy skinning implementation](https://github.com/ScanMountGoat/xc3_lib/blob/main/xc3_model/src/skinning.rs) uses ordinal 0. Also, flags 1/8 can concatenate two shared buffers, even if both resolve to the same buffer. v0.7.2 reproduces this mapping instead of requiring a raw header slot to match an original vertex buffer index. **Source weights and HKT remapping remain validated; unsupported skins keep MeshRenderer.**

**Test v0.7.2**: reimport `dl019100.camdo`, confirm import summary `Skell weight import: X SkinnedMeshRenderer draw calls, Y static fallbacks`, inspect actual deformation by rotating one bone on a scene instance, and run **Audit Skell Bone Weights** to see resolved original weight selector values. The exact original bone weighting is not yet verified by rendering and deformation tests.


## Skell bone weights and SkinnedMeshRenderer preview (v0.7.1)

**Requested priority:** bone/weight/rig first, then **Skell texture decoding**, then **animation decoding**. v0.7.1 only changes the bone phase.

The new **Rig > Import Skell Bone Weights** option (OFF by default) tries to convert a draw call from static MeshRenderer to SkinnedMeshRenderer using original Wii U MXMD skin metadata:

- \`WeightIndex\` and \`WeightIndex2\` attributes (types **3/4**, Uint16x2) in a vertex buffer reference a row in a shared weight buffer.
- \`SkinWeights2\` (type **1**, Float32x3 with 4th component reconstructed) and \`BoneIndices2\` (type **2**, Uint8x4) are stored in separate shared vertex buffers.
- Legacy VertexData contains six weight buffer selector indices. Selected buffers are determined by mesh \`flags2\` from the MXMD mesh descriptor, with cautious fallback for unsupported selectors.
- Skinning indices refer to **MXMD bone-name palette order**. The importer resolves each palette entry by **name** to the already-imported sibling \`*_rig.hkt\` skeleton.
- Successful draws use a duplicate Unity Mesh with \`BoneWeight[]\`, computed inverse-bind matrices, and SkinnedMeshRenderer. **Unsupported draw calls remain intact as MeshRenderer**, preserving existing geometry.
- This is **not** auto-weighting/proximity skinning. No guessed bone assignments are generated.

**Test:**
1. Update from \`main\`. Select \`dl019100.camdo\`, choose **Rig** and enable **Import Skell HKT Hierarchy** and **Import Skell Bone Weights**; click Apply/Reimport.
2. Inspect the imported root: \`Skell_Rig\` should be present, and successful draws should use **SkinnedMeshRenderer**.
3. In Console inspect \`Skell weight import: X SkinnedMeshRenderer draw calls, Y static fallbacks\` and the first eight fallback causes.
4. Right-click the selected CAMDO in Project > **XBX2Unity > Audit Skell Bone Weights**, or **Tools > XBX2Unity > Audit Selected Skell Bone Weights**. The report is written to \`XBX_Exports/SkellAudits/dl019100_skin_weights.txt\`, listing HKT vs MXMD skinning bone names, vertex attributes, weight table buffer indices, and mesh flags.

**Important:** This importer has been implemented based on the xc3_lib legacy format code, but no Unity scene test of actual weight deformation has been performed in the connected environment. If the MXMD bone names do not match the HKT skeleton, or the source uses unexpected buffer layouts, skinning remains disabled for affected draws. Share the audit report plus the Console fallback counts for precise follow-on fixes. This stage does not implement MTXT decoding or AnimationClips.

Reference: [ScanMountGoat/xc3_lib](https://github.com/ScanMountGoat/xc3_lib) (\`xc3_lib/src/vertex.rs\`, \`xc3_lib/src/mxmd/legacy.rs\`, \`xc3_model/src/skinning.rs\`).


## Native Skell pipeline (v0.7.0, first working stage)

The `chr_dl.cpk` archive contains model groups such as `dl019100.camdo`, `dl019100.casmt`,
`dl019100.camtp`, `dl019100_rig.hkt`, and `.cea/.ces/.efp` companions. The Unity folder audit on November 2, 2026 observed **19 CAMDO models with a combined 968 successful static draw calls**, 19 HKT skeleton files and no separately extracted PNG textures.

### Added in this release

- **HKT hierarchy preview:** the CAMDO importer now looks for a same-name `*_rig.hkt`, verifies Havok packfile header `57 E0 E0 57 10 C0 C0 10`, reads named bones, parenting, local translation/quaternion/scale from the documented big-endian HKT subset, and creates a `Skell_Rig` transform hierarchy. This is enabled in the **Rig** tab. The HKT parser is new and has not yet been tested with the user's raw binary files; if an unexpected variant is detected, it logs a warning and still imports the static meshes.
- **Skell resource audit:** select a Skell `.camdo` and choose **Assets > XBX2Unity > Audit Skell Textures Rig Animation** or **Tools > XBX2Unity > Audit Selected Skell Resources**. The audit is read-only and writes `XBX_Exports/SkellAudits/<model>_skell_resources.txt`. It lists CAMDO/CASMT/CAMTP/HKT/CEA/EFP sizes and magic, skeleton bone/parent names, candidate valid big-endian MTXT footer records inside CAMDO/CASMT (dimensions, GX2 surface format, tile mode), and related CES event files.
- **Rig and Animation Inspector tabs:** advertise precisely what is implemented and provide one-click source audits.

### Status and what is needed to finish

| Feature | Status |
| --- | --- |
| Skell CAMDO static geometry | Working on 19 models in current Unity logs |
| Skell HKT transform/bone hierarchy | Reader implemented; **pending actual HKT runtime verification** |
| True SkinnedMeshRenderer with weights/bindposes and bone mapping | **Not implemented** |
| Skell texture extraction from MTXT GX2 surfaces | **Not implemented** (metadata audit only); GX2 deswizzle and BCn decode are required |
| CEA/CES source report | Implemented read-only metadata inventory |
| Real keyed animations / humanoid or generic Avatar | **Not implemented**, no fabricated motion tracks |

To continue accurate decoding, share the generated `dl019100_skell_resources.txt` report. It will identify the HKT parse status and raw MTXT format metadata. Additional parsing of the original Skell `.camdo/.casmt/.hkt` and actual animation stream bytes is needed for a finished rig and clips. Source reference: [xc3_lib's Wii U MTXT and HKT parsers](https://github.com/ScanMountGoat/xc3_lib/tree/main/xc3_lib/src).

**Source safety:** this importer never edits game archives or claims a static skeleton preview is a working skinned Skell.


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
