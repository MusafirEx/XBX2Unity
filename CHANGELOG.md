# Changelog

## 0.13.3 - 2026-11-06 (source-observed Skell RGB red metallic mask)
- User confirmed the source Skell `*_RGB` red channel represents **metallic coverage**; green remains body recoloring and blue remains glow. Alpha and game roughness/smoothness are still unidentified, and the historical four-zone RGBA paint hypothesis remains experimental.
- Extend existing `XBX2Unity/Skell Layer Preview` shader with independent opt-in **Red Metallic Surface Preview** (`_UseObservedRedMetallic`, `_MetallicStrength`, `_MetallicMaskGain`, `_MetallicSmoothness`, `_MetallicHighlightStrength`). Decode red directly from the previously verified **linear BC1 _RGB companion mask**; metallic is proportional to red coverage (0=unchanged, 1=metal).
- Use transformed vertex/world normals and camera direction for a **GGX-like highlight + Fresnel and illustrative environment gradient** in both Built-in and URP diagnostic passes. This demonstrates reflective metal surface appearance, but is **not full Unity Lit PBR**, native Xenoblade X BRDF, or accurate scene reflection-probe/lighting integration; smoothness is manually adjustable because its original texture channel is unknown.
- Preserve green body color tint and blue emissive overlay **as independent channels**; body tint precedes red reflection, emission follows it. Obsolete RGBA four-color paint experiment is bypassed when *any* confirmed RGB role is enabled.
- Add **two append-only debug views** (14=Red Metallic grayscale, 15=source R/G/B overlay), preserving prior modes 0–13; connect all new settings in CAMDO Material inspector/generation path, without mutating external `.mat` overrides or Skell HKT/rig/geometry.
- Update linear-mask and observed-channel CI regression checks, README and source texture role description. Real Unity 2022 shader compilation and visual quality still need user-side validation.


## 0.13.2 - 2026-11-05 (SAR mixed-skeleton recovery and actionable 0-clip diagnostics)
- Analyze real 7,348,224-byte `dl100100.sar`: valid SAR1 BE v0x101, 444 in-bounds Havok splines; 437 `dl100100_Bone` motions (104 tracks), four `dl019600_Bone` motions (83 tracks), three `dl049100_Bone` motions (115 tracks), of which six are unsupported multi-block.
- Independently validate whole-payload parsing of **438** single-block HKT entries. A lack of Unity AnimationClips is thus not evidence of corrupt SAR. A valid skeleton/CAMDO target is still required for exact Transform paths.
- Fix SAR importer early abort: missing, incompatible or unimported primary CAMDO no longer prevents inspection/import of other embedded HKT entries. Determine and cache matching rig/CAMDO **by each HKT's own skeleton name**, optionally fallback from an incompatible explicitly chosen primary to the matching imported source-family CAMDO.
- Manifest explicitly records source skeleton families/track counts, missing-rig lookup errors, skipped entries and prominent zero-clip warnings, without fabricating incorrect AnimationClip curves or rewriting source data.
- Retain original `motion_####` sub-asset IDs and compatible target variants, with regression contracts for mixed 83/104/115-bone archives, partial rig availability and no write/extraction.
- Real Unity Editor compilation and clip playback remain to be tested by the user.


## 0.13.1 - 2026-11-05 (compatible Skell HKT/SAR animation sharing)
- Binary compare the user's two actual CAMDOs: `dl099100` and `dl099110` are both 827,392 bytes (MXMD) and have **228 identical bone-name string occurrences with the same offsets**; root names differ (`dl099100_Bone` versus `dl099110_Bone`). Full skeleton hierarchy/bind pose equivalence is NOT asserted without the original `*_rig.hkt` files.
- Fix imported animation root path mismatch by allowing only `dlXXXXXX_Bone` → `dlYYYYYY_Bone` root alias, while enforcing exact nonroot bone name and index matches, target transform path checks, 1:1 target skeleton size, and parent -1 on the root.
- Add **Compatible Skell CAMDOs** list to HKT/SAR ScriptedImporter inspectors. A single HKT or SAR can produce original clips plus target-rooted compatible `AnimationClip` sub-assets, without external .anim files and without duplicating compressed spline decoding per target.
- Keep original SAR `motion_####` sub-asset identifiers stable. Compatible variants use target CAMDO GUID suffix, so swapping target order does not break Animator references.
- Add regression contracts for accepted/rejected root aliases, exact joint-name checks and new variant IDs. No source HKT/SAR/CAMDO/rig/material asset overwrites.
- Limited to verified single-block spline codec; visual playback and rig bind-pose parity require Unity Editor/user validation.


## 0.13.0 - 2026-11-05 (automatic HKT and SAR animation ScriptedImporters)
- Add native Unity `ScriptedImporter` handlers for `*.hkt` and `*.sar`: directly use original HKT as an AnimationClip asset, or expand SAR as a clip container, no manual extraction/export to `.anim`.
- Parse SAR archive entries **in memory** after validating header and bounds, preserving original input files and avoiding 444 temporary HKT files. Import all supported motions as named `AnimationClip` sub-assets; stable IDs use original SAR entry index, with optional name substring filtering and max clip count.
- Reuse verified HKT spline decoder, 103-track binding logic, CAMDO hierarchy validation, native `flipX` / `modelScale`, optimized static curves and seamless idle looping from existing v0.12.2 manual exporter.
- Resolve CAMDO via explicit Inspector assignment or automatic name matching (HKT skeleton root name or SAR filename), and read the sibling `*_rig.hkt` for exact bone hierarchy. `*_rig.hkt` itself imports as skeleton source information, never a motion clip.
- Show an import manifest for SAR with imported/skipped/unsupported counts; rejected files fail visibly instead of creating fabricated animation.
- Add HKT/SAR inspector settings, AssetImportContext dependencies on matching CAMDO/rig, in-memory native HKT probe with suppressed Base64 debug output, source contract tests and Unity package mappings.
- Decoder limitations remain: unsupported Havok codecs/multi-block clips are skipped. Actual Unity 2022 Editor import/playback must be tested on user project; CI checks synthetic fixtures/package construction only.


## 0.12.2 - 2026-11-05 (Skell clip root / renderer binding diagnostic)
- Investigate reported **no visible animation** despite exported native Skell `.anim`. The user-supplied idle clip contains 13 varying rotation groups and one varying position group; `park_st` contains 64 varying rotation groups and two varying position groups, so the sampled clips are **not empty**.
- Both clips encode 103 × 3 identical `Skell_Rig/<bone hierarchy>` paths. These paths **require the Animator to be on the CAMDO model root**, one parent ABOVE `Skell_Rig`, not on the rig or a detached model child.
- The submitted `park_st` serialized `m_ClipBindingConstant.genericBindings: []` despite 309 grouped Transform curves; the submitted idle clip has **309** generic binding entries. This discrepancy is suspicious but not definitive proof of the playback problem.
- Add **Tools > XBX2Unity > Animation > Diagnose Skell Clip / Hierarchy** editor window to analyze Unity-recognized curve bindings, exact paths against a selected imported CAMDO, root/nested Animator placement/controllers, and SkinnedMeshRenderer bones/weights versus static MeshRenderer fallback. Provides explicit read-only error reporting, opt-in clip reimport and temporary scene pose preview.
- After exporting a **new** native .anim, force a clip reimport to refresh serialized bindings and log an explicit warning if Unity still reports zero curve bindings. Warn when the target CAMDO prefab has no skinned renderers.
- Preserve actual HKT motion samples, model/rig/material source assets and existing AnimationClips. Tests are source contracts/synthetic; actual Unity editor compilation and scene playback require end-user verification.


## 0.12.1 - 2026-11-05 (compact Skell .anim curves + seamless idle looping)
- Independently audit the user's real generated `0000_000000_idle_anm_native.anim` (20,683,697 bytes) against the original 11,200-byte HKT and the 444-entry `dl099100.sar`: the HKT appears byte-for-byte in the SAR, all 103 annotation names match exported clip paths, and all 309 quaternion/position/scale curves contain 201 ordered samples. The original file contains no invalid values or quaternion sign discontinuities; only 13 quaternion groups and one position group vary over time.
- Fix exporter bloat: preserve all dynamic samples, but replace redundant static **whole-vector/quaternion groups** with two identical endpoint samples. On the submitted idle clip this reduces 62,109 serialized keyframes to 3,404 (295 static groups, 14 dynamic groups). A directly transformed YAML fixture shrank 20.68 MB to 1.59 MB (**92.33% smaller**) with all original dynamic curve values and timestamps preserved.
- Enable Unity's `AnimationClipSettings.loopTime` only for an explicit `idle`-named motion whose first and last decoded per-bone position/scale/quaternion poses agree within strict tolerances. Other animations remain non-loop by default.
- Keep the exact HKT motion decoder, track-to-bone mapping, original CAMDO, rig and material assets unchanged.
- GitHub CI covers synthetic compression/loop contracts and package build; actual Unity Editor .anim compilation and visual playback still require validation.


## 0.12.0 - 2026-11-04 (native HKT spline AnimationClip exporter)
- Independently reconstruct the original Wii U idle motion compressed `m_data` (2416 bytes; SHA-256 `720c4647a07950042e2cab571614603540d2c01053be713d57319e959635bd30`) from the user's uploaded Phase 2E report. Confirm exact end-to-end parsing of 103 transform tracks and repeated idle pose samples, with 13 dynamic quaternion tracks and one dynamic translation track.
- Implement a bounded decoder for single-block Havok 2013 native 32-bit spline-compressed animation: 4-byte track masks, static or dynamic quantized translation/scale, little-endian spline knots, 40-bit compressed quaternion reconstruction and de Boor interpolation.
- Add explicit Unity Editor `Convert Selected HKT to Unity AnimationClip` command. Select the matching imported Skell CAMDO with sibling `*_rig.hkt`, verify exact track-to-bone index/name matches and prefab hierarchy, then create **a NEW** generic `.anim` with sampled Transform position, quaternion rotation and scale curves.
- Honor the CAMDO importer's `flipX` and `modelScale`, maintain quaternion sign continuity, refuse to overwrite an existing asset or alter the Skell model, rig, HKT or material files.
- Add new Editor script metas, exporter button under the CAMDO Animation tab, package build mappings, and synthetic 40-bit quaternion/de Boor, exporter contract tests.
- Limits: multi-block animations, other quaternion quantization modes, separate root-motion extraction, generic-to-Humanoid retargeting and actual Unity Editor compilation/Animator playback need follow-up verification.


## 0.11.5 - 2026-11-04 (HKT Phase 2E source annotation and packed-data audit)
- Correct Phase 2D interpretation: the 103 consecutive 16-byte pointer targets contain printable `hkaAnnotationTrack` name strings (e.g. `dl099100_Bone`, `EX_UVA`), **not** compressed keyframe records.
- Verify 103 bounded ASCII name entries against the animation annotation array pointer/count/capacity, list their names beside the existing track-to-bone index mapping as an explicitly unverified positional association.
- Recognize, with strict count/bounds/alignment checks, a 2,448-byte packed interval comprising two 16-byte offset tables and a **2,416-byte `m_data` compressed source blob** for the user's single-block idle sample.
- For individual HKT audits, include compressed data SHA-256, first/last 64 bytes and exact Base64 for sources at most 8 KiB so the data can be reverse-engineered without manual hex extraction.
- Skip unsupported layouts with read-only diagnostics; no HKT writes or fictitious AnimationClip/keyframes.


## 0.11.4 - 2026-11-04 (HKT Phase 2D raw stream reconnaissance)
- Confirm from the user's full `0000_000000_idle_anm.hkt` audit that its **103 track-to-bone entries are a unique permutation of 0–102**; binding and frame timing remain verified.
- Add a **read-only** diagnostic for individual HKT files: raw spline header prefix, consecutive 16-byte local pointer-run analysis and samples, bounded unclassified interval previews, and first/last root-reference-frame sample previews.
- Explicitly label byte/float previews as uninterpreted; do not claim knowledge of compressed B-spline data layout or root-motion values from a text report.
- No AnimationClip or keyframes created, no original game files modified.


## 0.11.3 - 2026-11-04 (HKT spline timing and binding metadata)
- From the user's real `0000_000000_idle_anm.hkt` Phase 2B audit, identify the Havok 2013 Wii U/32-bit spline metadata: 6.6667s, 201 frames at ~30 FPS, 103 transform tracks, zero float tracks and one compressed block. These are **header fields**, not decoded keyframes.
- Validate `hkaAnimationBinding` fixups to its animation object, original skeleton name `dl099100_Bone` and 103-element int16 track-to-bone mapping. The first track indices are 0, 62, 63, 1, 64, 65, 66, 2. Export the complete mapping to the report when one HKT is selected.
- Expose root reference-frame sample count (201), check 16-byte record bounds and avoid making unsupported root-motion displacement claims.
- Keep invalid/unrecognized motion field formats as metadata warnings, preserving the previously validated Havok packfile status. Unity AnimationClip and spline keyframe decoding remain pending.


## 0.11.2 - 2026-11-04 (Skell motion Phase 2B probe)
- Real Wii U `0000_000000_idle_anm.hkt` successfully validated as `hk_2013.1.0-r1`, BE/32-bit, 48-byte section header, offsets at +0x14, root `hkRootLevelContainer`; classes include animation container, binding, spline codec and default animated reference frame.
- Extend the existing read-only HKT audit to emit verified typed object offsets and validated local/global pointer locations; for a single HKT, include up to 96 raw bytes per typed object and up to 32 outgoing fixup pointers per object.
- Make valid section layout a report metadata line, not a warning. Expand synthetic section/pointer tests for all supported layouts and both endian forms.
- Native Havok spline keyframes, track bindings, duration and Unity AnimationClip creation remain unimplemented; no model/rig/material assets are modified.


## 0.11.1 - 2026-11-03 (Havok section-header layout investigation)
- Investigate real extracted `0006_001012_hv_park_ed_anm.hkt`: the Phase 2A audit reported `Bad section bounds: __types__`.
- Try validated Havok section layouts 48-byte/+0x10, 48-byte/+0x14, and 64-byte/+0x14 rather than silently treating every archive as one layout; reject layouts that fail any section name/offset check.
- On failure report candidate format plus raw section offset words in hexadecimal, enabling diagnosis without assuming the HKT is corrupt.
- Expand synthetic fixture regression tests to cover both endianness variants and all three layouts. No game HKT is modified, and animation tracks remain undecoded.


## 0.11.0 - 2026-11-03 (Skell animation Phase 2A)
- Add read-only **Havok 2013 HKT animation packfile metadata audit** for one extracted `.hkt` or a selected folder (up to 4096 files), intended for the user's 444 motion entries from `dl099100.sar`.
- Parse binary packfile header, little/big endianness, section descriptors, root object reference, bounded local/global/virtual fixup tables, and virtual class names. Report `hkaSplineCompressedAnimation`, `hkaAnimationBinding`, `hkaAnimationContainer` class references per file and in aggregate.
- Integrate the audit into the CAMDO Animation tab, Tools menu and Project context menu; generate reports outside Assets to avoid modifying source HKT, rig, geometry or `.mat`.
- Add synthetic Havok section/fixup endian/bounds tests and package manifests. Real spline/keyframe data, durations, bone-track binding and Unity `AnimationClip` conversion remain unimplemented until verified against native sample bytes.


## 0.10.1 - 2026-11-03 (Wii U SAR1 parser fix)
- Fix first in-editor failure: original Wii U `dl/*.sar` begins with `SAR1` (big-endian), not the `1RAS` (little-endian) magic used by other Xenoblade platforms. Read endianness from magic; explicitly handle `0x101` and `0x102` (64-byte entries) and `0x210` (96-byte entries).
- For audit and extraction, validate each entry offset and size against available physical bytes. A declared archive size mismatch alone now produces an audit warning rather than rejecting archives that might have all entries present; invalid entries still stop parsing/extraction.
- This corrects binary source discovery only: real Havok bone animation decoding and Unity `AnimationClip` generation remain future work. Validate `dl019600.sar` or another original Skell motion archive locally.


## 0.10.0 - 2026-11-03 (Skell Animation Phase 1)
- Identify the source of Wii U Skell battle animation outside CAMDO: `hkc.cpk/dl/<skell-id>.sar` typically contains named Havok `.hkt` motion entries, whereas `*_rig.hkt` beside CAMDO is the skeleton. CEA/CES remain event/auxiliary resources and are not misidentified as keyframe data.
- Add `XBXSkellSarAnimationAudit` read-only inspection of Wii U `SAR1` big-endian and `1RAS` little-endian archives, with bounded entry counts, size/offset validation, explicit 0x101/0x102 (64-byte) and 0x210 (96-byte) table handling, entry names, data magic and cautious Havok animation codec class-name hints. The initial 1RAS-only check was found incorrect by Unity Editor.log and corrected; live Wii U archive validation remains pending.
- Add explicit extraction of raw SAR entries without writing back to source game archives, using sanitized filenames and non-overwriting output. The Animation tab now has Audit Original Skell .sar and Extract Raw Animation HKT Entries actions. Audit logs go to `XBX_Exports/AnimationAudits/` (outside imported Assets).
- No fake AnimationClips or invented bone keyframes. Actual Havok animation clip decoding, timing, bone paths and root motion require examination of extracted Skell motion HKT samples. Unsupported SAR variants report the observed header instead of guessing.
- All existing model, HKT rig, skinning, texture/channel mapping and external `.mat` code remain unchanged. CI checks source contracts and synthetic 1RAS table golden; live animation HKT and Unity editor validation still outstanding.


## 0.9.4 - 2026-11-03
- Use a separate linear-space BC1 `_RGB` companion texture when sampling Skell paint/body/glow channel masks. Before this fix, source mask RGB values were imported as sRGB color and gamma-decoded by the GPU, altering mid-range coverage and the effect of mask gain/threshold.
- Keep the original sRGB MTXT texture and its existing MXMD/albedo selection intact; only the generated layer-preview `_PaintMask` binding uses the linear twin. No change to the original CAMDO/CASMT data or BC1 bytes.
- Apply to both packed and streamed MTXT records; register the linear masks as distinct CAMDO subassets and release replaced streamed records. No changes to rig, HKT skinning or external `.mat` overrides.
- ScriptedImporter revision 15, package v0.9.4, expanded source-contract CI tests. Requires local Unity 2022.3 recompilation and visual comparison on `dl099110.camdo`; Red-channel semantics remain unknown.

## 0.9.3 - 2026-11-03
- From direct in-game comparison of dl099110, the user identified the *Green* channel in *_RGB companion as the **Skell body mask**, and *Blue* as the **glowing/luminous parts mask**. Red is **still unidentified**. This observation does not prove global consistency across every Skell.
- Add independent opt-in Green -> Body Color Tint and Blue -> Glow Emission Preview controls. Both default OFF; body defaults white with adjustable coverage/gain and glow defaults light blue with adjustable intensity/gain.
- The XBX Skell Layer Preview shader uses source G only for body tint and source B only for additive emission. Existing legacy four-zone RGBA experiment remains available but is explicitly bypassed when either user-observed role is active. No assumptions are made about Red or BC1 alpha as extra editable paint zones.
- Add raw G (body), raw B (glow), and G/B false-color overlay views to the existing 11 preview modes. Source *_RGB is still bound for read-only viewing without enabling either effect.
- MXMD material indices, BC1/BC5 deswizzle, normal/SPM maps, external .mat overrides and HKT skinning remain unchanged. New controls only affect generated Skell Layer Preview materials; existing extracted .mat assets are not edited.
- Shader uses unlit additive glow to visualize the candidate mask; true light emission, HDR bloom and the game's shader effects are NOT reproduced.
- ScriptedImporter revision 14, package v0.9.3, new source-role regression in CI. GitHub Actions cannot compile the Unity C#/HLSL shader.


## 0.9.2 - 2026-11-03
- Add evidence-based Skell paint-mask forensic workflow, not an assumed native shader. Unity dl099110 logs show 28 decoded MTXT images, 6 RGB companion material links and 60 vertex-color buffers; this does NOT prove paint-zone/channel mapping.
- Sample deswizzled BC1 DXT1 RGB/A channels before the CPU-readable texture data are discarded. Retain min/mean/max and near-zero/near-one fractions and chroma percentage in original MXMD material manifest for each referenced BC1 texture. Sampling is capped at roughly 16K pixels per texture.
- Show _RGB companion channel summaries in Material Inspector and add Export RGB Companion Channel Audit (.txt) for source material/texture indices, no source file modification.
- Add mask R/G/B/A grayscale, threshold, normalized RGB dominance, alpha, raw vertex color and source base-vs-companion difference views in Skell Layer Preview.
- Fix v0.9.1 limitation: allow source _RGB to be bound for READ-ONLY shader diagnostics independently of opt-in paint. Tint still defaults OFF and strength zero; external .mat overrides remain untouched.
- Clarify that BC1 alpha is binary 0/255, not a fully independent fourth grayscale channel. Original four-zone Skell paint mapping is unverified.
- Bump ScriptedImporter to 13; add sampled BC1 regression and CI package tests. Preserve working GX2 deswizzle, material remaps, HKT skinning, animations and UV logic.
- CI is not a Windows Unity shader compiler; test the package in the Unity editor.


## 0.9.1 - 2026-11-03
- Extend v0.9.0 MXMD material manifest after the user confirmed external .mat overrides remain persistent after reimport.
- Resolve texture roles by source MXMD per-material IDs and name/BCn cues: BC5 NRM, BC1 SPM, BC1 RFM/REF, BC1 GLO/GLW and BC1 *_RGB companion (paint meaning unverified). Add independent role slot indices and readable role labels in the Material Inspector.
- Add opt-in "Bind BC5 Normal Candidates" and "Bind SPM Specular Candidates" for **generated** Unity material previews, using available shader _BumpMap/_NormalMap and _SpecGlossMap properties. Explicit normal/spec binding is OFF by default; no changes to user-owned external .mat files.
- Add optional unlit XBX2Unity/Skell Layer Preview shader to visualize base color, BC5 normal channels, SPM specular image, and raw *_RGB companion candidate. This is diagnostic, not the native Wii U illumination model.
- Provide a strictly opt-in experiment to interpret RGBA from *_RGB companion as four paint zones. All four paint colors default white, paint strength 0, and mask interpretation OFF until manually enabled. Do not claim real Skell paint zones are proven.
- Keep source reflection and glow texture roles visible as metadata only, without guessing their shader bindings.
- Bump CAMDO ScriptedImporter revision to 12. Preserve v0.9.0 material overrides/extraction, verified texture mapping and bone weights. Animation remains a later phase.
- Add role fixtures/contract tests and build packaging for new C# class and shader. GitHub CI is not a Unity shader/compiler test.


## 0.9.0 - 2026-11-02
- Adopt reusable Unity material workflow patterns from MusafirEx/EMD2Unity, MusafirEx/Unity-CnC-SAGE-Tools (W3X/W3D), and MusafirEx/Bress2Unity without copying their game-format assumptions.
- Add XBXMaterialManifest per CAMDO import, carrying stable MXMD material index, source color, original texture-slot IDs, decoded MTXT names/formats/usage hints, selected albedo slot, associated generated material, assigned material and renderer count.
- Add Material Inspector table with per-MXMD-material source links, generated material preview, editable external .mat override, and batch/per-material Extract .mat actions. Extraction clones the generated material to Assets/<model>_Materials and attaches an index-keyed override; original game files remain read-only.
- Add generated Unity shader override and choose default Standard/URP/HDRP shader based on the active rendering pipeline, like EMD2Unity. Explicit override takes precedence over experimental diagnostic shader options.
- Apply external .mat overrides only after creating internal native material previews, so users can compare original MTXT bindings and edited materials. Accept only Assets/*.mat, reject CAMDO sub-assets, register asset dependencies and preserve source material identity.
- Bump CAMDO ScriptedImporter to revision 11. Preserve all v0.8.4 GX2 deswizzle, MTXT color/paint comparison, gMatCol, UV settings, HKT rig and weights. No changes to animation decoding.
- Add CI contract checks for material manifest, extraction/override structure and packaged source files.
- GitHub Actions checks source invariants and packages but cannot verify C# compilation or Unity Inspector usability; test locally in Unity 2022.3.


## 0.8.4 - 2026-11-02
- Fix the source-albedo preference for dl109100: the importer always preferred *_RGB companion textures instead of the unsuffixed primary map for five of seven materials. Original Skell color customization may use RGB companion maps in a separate shader pass; the exact semantics are not yet verified.
- Make primary unsuffixed BC1 textures the default albedo choice. Add explicit **Prefer Skell _RGB as Albedo** option (OFF by default) under Material > Auto Select Skell Color Texture for comparing behavior. Manual source material slot override remains available.
- Ensure non-color role tokens such as RFM_B/NRM_B/GLW_B are excluded regardless of suffix variant; previously a generic usage=32 let DL019600_RFM_B count as color.
- Add seven dl109100 source-material regression fixtures to CI, testing both primary and *_RGB modes; this test checks source-selection heuristics, not final visual shader equivalence.
- Bump ScriptedImporter revision 10 for automatic reimport. Preserve v0.8.3 GX2 deswizzle, skinned rig, bindposes and geometry unchanged.
- Actual in-game Skell paint masks and native shader blending are outside this fix.


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
