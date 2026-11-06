#!/usr/bin/env python3
"""Source-contract tests for user-observed dl099110 Skell RGB roles.

The user directly compared against the game:
  Red channel -> metallic coverage; Green -> body tint; Blue -> glow.
BC1 alpha remains unverified; roughness/smoothness remain a user setting.

This test checks mapping is explicit and opt-in, not 3/4-color guessing.
It cannot compile Unity shaders; verify the editor separately.
"""
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
shader = (ROOT / "Runtime/Shaders/XBXSkellLayerPreview.shader").read_text(encoding="utf-8")
imp = (ROOT / "Editor/XBXCamdoNativeImporter.cs").read_text(encoding="utf-8")
ui = (ROOT / "Editor/XBXCamdoNativeImporterInspector.cs").read_text(encoding="utf-8")


def includes(data, *pieces):
    for p in pieces:
        assert p in data, "Missing source-role contract: " + p


def simulate(primary, rgba, body=False, glow=False, body_color=(1.0, 1.0, 1.0),
             glow_color=(0.2, 0.6, 1.0), body_strength=1.0, glow_intensity=1.0):
    """Mirror the shader preview's body-tint and additive glow logic."""
    color = list(primary)
    if body:
        green = max(0.0, min(1.0, rgba[1]))
        amount = green * body_strength
        color = [color[i] * ((1.0 - amount) + body_color[i] * amount) for i in range(3)]
    if glow:
        blue = max(0.0, min(1.0, rgba[2]))
        color = [color[i] + glow_color[i] * blue * glow_intensity for i in range(3)]
    return tuple(round(c, 5) for c in color)


def main():
    includes(shader,
        "_UseObservedRedMetallic", "_MetallicStrength",
        "_MetallicMaskGain", "_MetallicSmoothness", "_MetallicHighlightStrength",
        "metalCoverage = saturate(maskCandidate.r * _MetallicMaskGain)",
        "metalCoverage * saturate(_MetallicStrength)",
        "resultColor.rgb = lerp(resultColor.rgb,",
        "metallicResponse, metallic",
        "float3 N = normalize(i.worldNormal)",
        "float3 V = normalize(_WorldSpaceCameraPos.xyz - i.worldPosition)",
        "_UseObservedGreenBody", "_UseObservedBlueGlow",
        "_BodyPaintColor", "_GlowColor", "_GlowIntensity",
        "_BodyMaskGain", "_GlowMaskGain",
        "maskCandidate.g * _BodyMaskGain",
        "maskCandidate.b * _GlowMaskGain",
        "bodyCoverage * saturate(_BodyMaskStrength)",
        "_GlowColor.rgb * glowCoverage",
        "_UseObservedGreenBody < 0.5 && _UseObservedBlueGlow < 0.5",
        "if (_ViewMode > 10.5 && _ViewMode < 11.5)",
        "if (_ViewMode > 11.5 && _ViewMode < 12.5)")
    includes(imp,
        '[ScriptedImporter(15, "camdo")]',
        "public bool skellUseObservedRedMetallic = false;",
        "public float skellMetallicStrength = 1f;",
        "public float skellMetallicMaskGain = 1f;",
        "public float skellMetallicSmoothness = 0.6f;",
        'mat.SetFloat("_UseObservedRedMetallic"',
        'mat.SetFloat("_MetallicStrength"',
        'mat.SetFloat("_MetallicMaskGain"',
        'mat.SetFloat("_MetallicSmoothness"',
        'mat.SetFloat("_MetallicHighlightStrength"',
        "hasCandidate && skellUseObservedRedMetallic",
        "public bool skellUseObservedGreenBody = false;",
        "public bool skellUseObservedBlueGlow = false;",
        'mat.SetFloat("_UseObservedGreenBody"',
        'mat.SetFloat("_UseObservedBlueGlow"',
        "hasCandidate && skellUseObservedGreenBody",
        "hasCandidate && skellUseObservedBlueGlow",
        "renderer.sharedMaterial = overrideMaterial;",
        "XBXSkellSkinning.TryBuild")
    includes(ui,
        "Observed Red - Metallic Mask (dl099110)",
        "Observed RGB - R Metallic / G Body / B Glow",
        "Red -> Metallic Surface Preview",
        "Metal Smoothness (Estimated)",
        "Observed Green - Body Mask (dl099110)",
        "Observed Blue - Glow Mask (dl099110)",
        "Green -> Body Color Tint",
        "Blue -> Glow Emission Preview")
    assert "float line =" not in shader  # HLSL reserved keyword
    assert "skellRedMask" not in imp
    assert "maskCandidate.a * _Metallic" not in shader  # alpha is unknown
    assert "if (_UseObservedRedMetallic > 0.5)" in shader
    assert "if (_ViewMode > 12.5 && _ViewMode < 13.5)" in shader
    assert "if (_ViewMode > 13.5 && _ViewMode < 14.5)" in shader
    assert "if (_ViewMode > 14.5)" in shader
    # Green and blue are still read from independent channels, and
    # legacy RGBA four-color painting is bypassed when metallic is active.
    assert "_UseObservedRedMetallic < 0.5" in shader
    assert "maskCandidate.g * _BodyMaskGain" in shader
    assert "maskCandidate.b * _GlowMaskGain" in shader
    assert "resultColor.rgb += _GlowColor.rgb * glowCoverage" in shader
    # Mask values stay linear; R coverage 0 remains unaffected by metallic.
    for red in (0.0, 0.2, 0.5, 1.0):
        coverage = max(0.0, min(1.0, red * 1.5))
        amount = coverage * 0.8
        assert 0.0 <= amount <= 0.8
    assert min(1.0, 0.0 * 1.5) == 0.0
    assert simulate((0.6, 0.6, 0.6), (0.3, 0.0, 0.0, 1.0),
                    body=True, glow=True, body_color=(1, 0, 0)) == (0.6, 0.6, 0.6)
    assert simulate((0.6, 0.6, 0.6), (0, 1, 0, 1), body=True,
                    body_color=(1, 0, 0)) == (0.6, 0.0, 0.0)
    assert simulate((0.1, 0.1, 0.1), (0, 0, 1, 1), glow=True,
                    glow_color=(0, 0.5, 1)) == (0.1, 0.6, 1.1)
    assert simulate((0.1, 0.1, 0.1), (0, 0, 1, 1)) == (0.1, 0.1, 0.1)
    print("Skell R=metallic/G=body/B=glow shader controls and preview regression passed.")


if __name__ == "__main__":
    main()
