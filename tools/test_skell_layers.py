#!/usr/bin/env python3
"""Skell v0.9.1 source-role/paint/override contract tests.

Source-level regression only: actual shader compilation and image correctness
must still be checked inside Unity 2022.3 on real CAMDO/CASMT.
"""
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
imp = (ROOT/"Editor/XBXCamdoNativeImporter.cs").read_text(encoding="utf-8")
role = (ROOT/"Editor/XBXSkellMaterialLayers.cs").read_text(encoding="utf-8")
manifest = (ROOT/"Editor/XBXMaterialManifest.cs").read_text(encoding="utf-8")
shader = (ROOT/"Runtime/Shaders/XBXSkellLayerPreview.shader").read_text(encoding="utf-8")
editor = (ROOT/"Editor/XBXCamdoNativeImporterInspector.cs").read_text(encoding="utf-8")
builder = (ROOT/"tools/build_unitypackage.py").read_text(encoding="utf-8")
material_ui = (ROOT/"Editor/XBXMaterialInspectorUI.cs").read_text(encoding="utf-8")

def contains(src, *needles):
    for n in needles:
        assert n in src, f"Missing source contract: {n}"

contains(imp,
    '[ScriptedImporter(12, "camdo")]',
    "public bool importSkellNormalMaps = false;",
    "public bool importSkellSpecularMaps = false;",
    "public bool skellLayerPreviewShader = false;",
    "public bool skellUseRgbCompanionAsPaintMask = false;",
    "public float skellPaintStrength = 0f;",
    "XBXSkellMaterialLayers.AssignNormal(mat, normalCandidate)",
    "XBXSkellMaterialLayers.AssignSpecular(mat, specCandidate)",
    "XBXSkellMaterialLayers.Role.RgbCompanion",
    'mat.SetTexture("_PaintMask", rgbCandidate.texture.texture);',
    'ctx.AddObjectToAsset("xbx_material_manifest", manifest);',
    "renderer.sharedMaterial = overrideMaterial;",
    "XBXSkellSkinning.TryBuild",
    "XBXSkellMtxt.Decode")
contains(role, 'Token(n, "SPM")', 'Token(n, "RFM")',
    'Token(n, "GLO")', 'EndsWith("_RGB"', 'r.format == 53',
    "material.HasProperty(\"_BumpMap\")",
    "material.HasProperty(\"_SpecGlossMap\")")
contains(manifest, "normalCandidateSlot", "specularCandidateSlot",
    "reflectionCandidateSlot", "glowCandidateSlot", "rgbCompanionSlot",
    "inferredRole")
contains(editor, "Bind BC5 Normal Candidates", "Bind SPM Specular Candidates",
    "Interpret _RGB as Paint Mask (Unverified)")
contains(material_ui, "entry.normalCandidateSlot", "entry.specularCandidateSlot",
    "entry.rgbCompanionSlot", "slot.inferredRole")
contains(shader, 'Shader "XBX2Unity/Skell Layer Preview"',
    "_PaintColor1", "_PaintColor2", "_PaintColor3", "_PaintColor4",
    "_UsePaintMask", "_SpecGlossMap", "_BumpMap", "_ViewMode")
assert "float line =" not in shader  # HLSL reserved identifier regression
assert "File.WriteAllBytes" not in imp and "File.WriteAllBytes" not in role
assert imp.index("renderer.sharedMaterial = overrideMaterial;") > imp.index(
    "XBXSkellMaterialLayers.AssignNormal(mat, normalCandidate)")
for name in ("Editor/XBXSkellMaterialLayers.cs",
             "Runtime/Shaders/XBXSkellLayerPreview.shader"):
    assert name in builder and name+".meta" in builder
    assert (ROOT/name).exists() and (ROOT/(name+".meta")).exists()

# Examples sourced from actual dl019100/dl019600/dl109100 MXMD logs:
# NRM is BC5; SPM=BC1; reflection and RGB companions must not silently
# replace albedo; source has independent texture IDs per material.
def roles(name: str, fmt: int) -> set:
    tokens = name.upper().split("_")
    result = set()
    if fmt == 53 and ("NRM" in tokens or any(t.startswith("NRM") for t in tokens)):
        result.add("normal")
    if fmt == 49:
        if "SPM" in tokens: result.add("spec")
        if any(t in tokens for t in ("RFM","REF","RF")): result.add("reflection")
        if any(t in tokens for t in ("GLO","GLW")): result.add("glow")
        if name.upper().endswith("_RGB"): result.add("rgb_companion")
    return result

fixtures=[
    ("DL019600_NRM_B",53,{"normal"}),
    ("DL019100_FL_NRMC",53,{"normal"}),
    ("DL109100_FL_SPM",49,{"spec"}),
    ("DL019600_RFM_B",49,{"reflection"}),
    ("DL019600_FL_GLW",49,{"glow"}),
    ("DL109100_FL_RGB",49,{"rgb_companion"}),
    ("DL109100_FL",49,set())
]
for name,fmt,expected in fixtures:
    assert roles(name,fmt)==expected,(name,roles(name,fmt),expected)
print("Skell role metadata and opt-in layer contract tests passed.")
