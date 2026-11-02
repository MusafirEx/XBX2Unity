# Changelog

## 0.8.3 - 2026-11-02
- Fix Unity 2022.3 D3D11 Skell diagnostic shader syntax error: local identifier `line` is reserved in HLSL. Rename it to `gridLineWeight` in the UV0 Checkerboard debug view.
- Add regression assertions to GX2 test workflow to catch the invalid shader identifier being reintroduced; the CI test does not replace actual shader compilation.
- Add missing Unity .meta for `tools/test_gx2_address.py` to remove the immutable Git package warning.
- Keep GX2 deswizzle fix, texture color selection, rig, skin weights and animation logic unchanged.


## 0.8.2 - 2026-11-02
- Fix a verified Wii U GX2 deswizzle address bug: BC1/BC5 D2-tiled macro pipe bit is y3 XOR x3. Previous v0.8.0/0.8.1 mistakenly used x4, scrambling 4x4 BCn block locations and making "Raw Color Texture" map incorrectly.
- Tighten source image semantics: _SPM, _RFM, _RF, _REF, _GLO, _GLW, _OCL and _DEP are never automatically used as color, even when MXMD texture usage code is a generic 32. Prefer explicit _RGB/_COL/_DCL/_AVA color maps over ambiguous bare names.
- Add texture-independent UV0 Gradient and UV0 Checkerboard views to Skell Color Diagnostic Shader. Expose Flip V on CAMDO UVs in Material tab to distinguish UV inversion from deswizzle.
- Bump CAMDO ScriptedImporter to revision 9 to reimport assets. Keep proven HKT skinning, weight mappings and all animation functionality unchanged.
- Add GX2 compressed block address regression test (golden offsets and tile collision scan) and run it before CI package build.
- Needs visual confirmation in Unity after reimport; passing CI tests do not establish exact original shader/paint fidelity.


## 0.8.1 - 2026-11-02
- Address v0.8.0 Skell MTXT images decoding but incorrect body colors.
- Parse legacy MXMD packed/streamed texture usage flags and source image names.
- Prefer BC1 color/usage hints when choosing automatic material albedo; reject BC5 and NRM images in base color slots.
- Read source Material.color gMatCol (four big-endian floats) and optionally use it instead of forcing all generated material tints to white.
- Add opt-in unlit Skell Color Diagnostic shader to separate texture, vertex color and material tint contributions.
- Emit bounded material/link audit diagnostics with format, source name, usage code, selected link and source color.
- Preserve native Skell HKT bone weights and SkinnedMeshRenderer. No animation changes.
- Bump ScriptedImporter revision 8. Package/build CI does not verify actual Unity color fidelity.


## 0.8.0 - 2026-11-02
- Begin Skell texture phase after user confirmed native skin deformation on two v0.7.2 models.
- Add opt-in GX2 MTXT reader: read referenced packed CAMDO textures and streaming CASMT low/high descriptor tables with their material IDs.
- Deswizzle the base mip for BC1 and BC5 in GX2 tile modes 2/4, preserving source native DXT1/BC5 compression in Unity Texture2D subassets.
- Bind source MXMD material texture indices to generated textures when available; keep map PNG logic separate.
- Add Inspector controls for native Skell textures and resource caps, diagnostics, and safe fallback for unsupported MTXT formats.
- ScriptedImporter revision 7; no changes to v0.7.2 skinning.
- Unity import, color semantics, high-res mapping and shader fidelity remain unverified until local test. CI validates packaging only.


## 0.7.2 - 2026-11-02
- Fix Skell native skinning selector mismatch identified from actual Unity Editor.log: dl019100 had 1 SkinnedMeshRenderer + 53 static fallbacks; dl019600 had 2 + 29.
- Root cause: v0.7.1 incorrectly treated legacy weight header selector values as absolute indices that must be present in the list of weight buffers. The actual legacy xc3_model implementation subtracts firstWeightBuffer, and when selector is before that start, uses the first weight buffer (ordinal zero).
- Match xc3_model/src/skinning.rs WeightGroups::Legacy: preserve shared weight-buffer row concatenation for skin flags 1 and 8, including repeated buffers, rather than silently deduplicating. No invented skin weights.
- Retain strict validation of weight row indices, finite weights, HKT palette remap, and fallback to MeshRenderer on unsupported draw calls.
- Add resolved weight selector diagnostics to the Skell weight audit. ScriptedImporter revision 6 forces reimport after upgrade.
- Real geometry deformation must still be tested in Unity; GitHub packaging CI only checks archives.


## 0.7.1 - 2026-11-02
- Focus exclusively on Skell **bone phase**; texture and animation decoders are deferred at the user's request.
- Read legacy MXMD bone-name skinning palette, shared skin buffer header indices and mesh render flags from CAMDO.
- Decode original WeightIndex/WeightIndex2 (u16x2) and shared SkinWeights2 (Float32x3; W reconstructed) / BoneIndices2 (u8x4) from Wii U big-endian vertex buffers.
- Remap **MXMD skinning bone names** to **HKT Transform names** rather than assuming their numerical order matches.
- Add opt-in Import Skell Bone Weights in the Rig Inspector: create separate SkinnedMeshRenderer and mesh bindposes for fully validated draw calls, otherwise retain original MeshRenderer/static geometry.
- Select shared weight buffers conservatively using the legacy mesh flags, with per-draw fallback diagnostics and no invented weights.
- Add independent read-only Skell Bone Weight Audit with palette/HKT name matching, vertex attribute enumeration, shared buffer slots and sampled WeightIndex ranges.
- ScriptedImporter revision 5. The native Unity import has not yet been verified against raw user Skell assets; CI only checks package creation.


## 0.7.0 - 2026-11-02
- Begin native Skell import support from chr_dl.cpk model sets (CAMDO + CASMT + CAMTP + HKT + CEA/CES).
- Add native big-endian Havok 2013 HKT skeleton reader (names, parent indices, TRS) following xc3_lib/hkt.rs.
- Add opt-in (enabled when HKT companion exists) skeleton Transform hierarchy preview under CAMDO imported root; failure leaves static geometry imported.
- Add read-only Skell Resource Audit, available in Assets and Tools menus and Model Importer Rig/Animation/Material Inspector tabs.
- Audit companion binary magic/size, HKT bone names/parents, MTXT GX2 texture footer metadata (version, format, dimensions, tiling), and related CEA/CES resources. Reports are saved to XBX_Exports/SkellAudits.
- No fabricated AnimationClips: motion tracks, HKT to mesh bone palettes, bone weights, bindposes and GX2 deswizzle/texture decoding are NOT implemented by this version.
- ScriptedImporter revision 4 to trigger reimport; package builder includes Skell tool scripts and metadata.


## 0.6.2 - 2026-11-02
- Add source mask selection for experimental terrain edge blending: MXMD vBlend, vertex color RGBA, base texture channel, overlay texture channel, or a constant diagnostic weight.
- Preserve secondary stage UV1 in the Unity mesh and allow overlay texture UV0 or UV1 sampling.
- Add mask inversion and diagnostic shader views (raw mask, thresholded weight, base/overlay, vBlend RGB) to distinguish absent mask, wrong channel, or wrong texture.
- Import summary logs count source vBlend buffers, vertex color and UV1, blended materials and distinct texture pairs.
- Add Tools/Assets > XBX2Unity > Inspect CAMDO Blend Data; writes bounded source mask min/max and linked material report to XBX_Exports/BlendAudits without changing game files.
- Keep experimental blending opt-in; the original Wii U per-material shader/output mapping remains unknown and must not be inferred from slot number.
- Bump ScriptedImporter version to 3 and include diagnostic editor tool in distribution packages.


## 0.6.1 - 2026-11-02
- Fix `MXMDMaterial_V1` array stride: 136 bytes, not 132. Confirmed with PredatorCZ/XenoLib legacy C++ layout.
- Prevent material-to-material pointer drift into float values such as `0x3F000000` observed in the user's `ma9990` Editor.log.
- Add material index, record address, base address, texture offset, link count, array count and file size to invalid-material pointer errors.
- Preserve successfully generated CAMDO/CASMT mesh geometry when material metadata is incomplete. Separate terrainLOD/Skybox decoder variants remain unsupported.
- Reimport a single CAMDO to verify texture binding; packaging CI is not Unity model validation.


## 0.6.0 - 2026-11-02
- Parse MXMD legacy material texture links and map external texture references (cached container / uncached global mapping).
- Resolve extracted stage PNG/DDS/TGA by exact container/id paths, bind base textures into native Unity Materials, and track imported texture dependencies.
- Read vertex `vBlend` (attribute type 14) into Mesh.uv3 independently from vertex color.
- Add opt-in two-layer edge blending preview shader for URP and Built-in, with selectable source slot, vBlend R/G/B/A channel, threshold and transition softness.
- Add Material Inspector controls and a diagnostic count of bound and unresolved textures.
- Preserve default single-layer materials; do not claim full original Xenoblade X shader recreation.
- Include Runtime shader and metadata in UPM and UnityPackage builds.


## 0.5.3 - 2026-11-02
- Read native map CAMDO **external geometry in the adjacent CASMT**, without GLTF.
- Recognize `MXMD VertexData[0x14] == 0` with `externalBufferIDs[0x28]` and `count[0x2C]` as a valid stage-extraction layout, rather than an invalid model.
- Decode terrain buffer pair/group lookup (`0xFFFFFFFF`) and object flat CASMT offsets as generated by XenoToolset casmExtract.
- Resolve each model group to its own CASMT geometry header, vertex/index buffer descriptors, UV, normals, and raw triangle indices.
- Preserve embedded geometry handling, add bounded size/offset checks, and include more specific failure diagnostics for malformed external lookup tables.
- CASMT textures/material shaders/rigging and map placement remain unsupported; real model validation requires Unity Editor.


## 0.5.2 - 2026-11-02
- Diagnose CAMDO MXMD 10040 'Missing Models/VertexData root pointers' using actual big-endian root field values and bounded header hex output.
- Add Assets > XBX2Unity > Audit CAMDO Header to save a read-only report without importing mesh, also accessible through Tools > XBX2Unity.
- Include file and CASMT companion sizes to distinguish absent source data from unsupported header layouts.
- Do not fabricate fallback pointers/geometry for unsupported variants; keep native ScriptedImporter and optional GLB viewer unchanged.
- CI packaging alone does not verify actual model contents; share the CAMDO audit report for precise follow-on decoder fixes.


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
