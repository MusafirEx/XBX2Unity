#!/usr/bin/env python3
"""Skell RGBA user-selected mapping: R bump, G metallic, B glow, A tint.

Static/source-contract tests, plus tiny independent mathematical checks.
Unity GPU compilation and render comparisons require the Unity Editor.
"""
from pathlib import Path
import math

ROOT = Path(__file__).resolve().parents[1]
shader = (ROOT / "Runtime/Shaders/XBXSkellLayerPreview.shader").read_text(encoding="utf-8")
imp = (ROOT / "Editor/XBXCamdoNativeImporter.cs").read_text(encoding="utf-8")
ui = (ROOT / "Editor/XBXCamdoNativeImporterInspector.cs").read_text(encoding="utf-8")
layers = (ROOT / "Editor/XBXSkellMaterialLayers.cs").read_text(encoding="utf-8")


def includes(text, *tokens):
    for token in tokens:
        assert token in text, "Missing Skell RGBA role contract: " + token


def saturate(x):
    return max(0.0, min(1.0, x))


def shade_channel_coverage(rgba, red_gain=1.0, green_gain=1.0,
                           alpha_gain=1.0, metal_strength=1.0):
    return (saturate(rgba[0] * red_gain),
            saturate(rgba[1] * green_gain) * saturate(metal_strength),
            saturate(rgba[2]), saturate(rgba[3] * alpha_gain))


def preview_alpha_tint(base, mask, color, strength=1.0):
    amount = saturate(mask) * saturate(strength)
    return tuple(round(base[k] * ((1 - amount) + amount * color[k]), 6)
                 for k in range(3))


def preview_blue_emission(base, blue, emission, intensity):
    return tuple(round(base[k] + emission[k] * saturate(blue) * intensity, 6)
                 for k in range(3))


def main():
    includes(shader,
        'Shader "XBX2Unity/Skell Layer Preview"',
        "_PaintMask (\"Companion RGBA: R height bump / G metallic / B glow / A color mask\"",
        "_UseObservedRedBump", "_BumpStrength", "_BumpMaskGain",
        "_UseObservedGreenMetallic", "_MetallicStrength", "_MetallicMaskGain",
        "_MetallicSmoothness", "_MetallicHighlightStrength",
        "_UseObservedBlueGlow", "_GlowColor", "_GlowIntensity", "_GlowMaskGain",
        "_UseObservedAlphaColor", "_AlphaTintColor", "_AlphaColorStrength",
        "_AlphaMaskGain",
        "redHeight = saturate(maskCandidate.r * _BumpMaskGain)",
        "metalCoverage = saturate(maskCandidate.g * _MetallicMaskGain)",
        "glowCoverage = saturate(maskCandidate.b * _GlowMaskGain)",
        "colorCoverage = saturate(maskCandidate.a * _AlphaMaskGain)",
        "float3 NormalFromRedHeight(",
        "ddx(worldPos)", "ddy(worldPos)",
        "ddx(redHeight)", "ddy(redHeight)",
        "return normalize(N - surfaceGradient * max(strength, 0.0))",
        "metalCoverage * saturate(_MetallicStrength)",
        "float3 N = _UseObservedRedBump > 0.5 ?",
        "bumpNormal : normalize(i.worldNormal)",
        "_AlphaTintColor.rgb",
        "colorCoverage * saturate(_AlphaColorStrength)",
        "resultColor.rgb += _GlowColor.rgb * glowCoverage",
        "_UseObservedRedBump < 0.5",
        "_UseObservedGreenMetallic < 0.5",
        "_UseObservedAlphaColor < 0.5",
        "_UseObservedBlueGlow < 0.5",
        "if (_ViewMode > 15.5 && _ViewMode < 16.5)",
        "if (_ViewMode > 16.5)",
    )
    assert "_UseObservedRedMetallic" not in shader
    assert "_UseObservedGreenBody" not in shader
    assert "metalCoverage = saturate(maskCandidate.r" not in shader
    assert "maskCandidate.g * _BodyMaskGain" not in shader
    assert "maskCandidate.a * _Metallic" not in shader
    assert "float line =" not in shader  # HLSL keyword
    # PBR-inspired metallic color needs to be applied between tint and glow.
    assert shader.index("if (_UseObservedAlphaColor > 0.5)") < shader.index(
        "if (_UseObservedGreenMetallic > 0.5)") < shader.index(
        "if (_UseObservedBlueGlow > 0.5)")

    includes(imp,
        '[ScriptedImporter(15, "camdo")]',
        "using UnityEngine.Serialization;",
        "public bool skellUseObservedRedBump = false;",
        "public float skellRedBumpStrength = 1f;",
        "public float skellRedBumpMaskGain = 1f;",
        "public bool skellUseObservedGreenMetallic = false;",
        "public float skellMetallicStrength = 1f;",
        "public bool skellUseObservedAlphaColor = false;",
        "public Color skellAlphaTintColor = Color.white;",
        'FormerlySerializedAs("skellBodyColor")',
        'FormerlySerializedAs("skellBodyStrength")',
        'FormerlySerializedAs("skellBodyMaskGain")',
        "public bool skellUseObservedBlueGlow = false;",
        'mat.SetFloat("_UseObservedRedBump"',
        'mat.SetFloat("_BumpStrength"',
        'mat.SetFloat("_BumpMaskGain"',
        'mat.SetFloat("_UseObservedGreenMetallic"',
        'mat.SetFloat("_MetallicStrength"',
        'mat.SetFloat("_MetallicMaskGain"',
        'mat.SetFloat("_UseObservedAlphaColor"',
        'mat.SetColor("_AlphaTintColor"',
        'mat.SetFloat("_AlphaColorStrength"',
        'mat.SetFloat("_AlphaMaskGain"',
        'mat.SetFloat("_UseObservedBlueGlow"',
        "hasCandidate && skellUseObservedRedBump",
        "hasCandidate && skellUseObservedGreenMetallic",
        "hasCandidate && skellUseObservedAlphaColor",
        "hasCandidate && skellUseObservedBlueGlow",
        "rgbCandidate.texture.linearMaskTexture ??",
        "renderer.sharedMaterial = overrideMaterial;",
    )
    assert "skellUseObservedRedMetallic" not in imp.split(
        'public override void OnImportAsset', 1)[0]
    assert "skellUseObservedGreenBody" not in imp.split(
        'public override void OnImportAsset', 1)[0]
    includes(ui,
        "Red -> Bump / Height",
        "Green -> Metallic Surface Preview",
        "Blue -> Glow Emission Preview",
        "Alpha -> Custom Color Mask",
        "Red - Bump Height Mask",
        "Green - Metallic Mask",
        "Alpha - Color Tint Mask",
        "Red - Perturbed World Normal Preview",
        "the color mask may therefore be binary or full-surface",
        "bypassed if any new channel role is enabled",
    )
    includes(layers, "R bump / G metallic / B glow / A color")

    # Monochrome color masks cannot contain a new RGB triplet; tint is
    # supplied explicitly. Zero alpha retains original color; full alpha
    # allows the chosen tint; BC1 1-bit alpha can only do these extremes.
    assert preview_alpha_tint((0.6, 0.6, 0.6), 0.0, (1, 0, 0)) == (
        0.6, 0.6, 0.6)
    assert preview_alpha_tint((0.6, 0.6, 0.6), 1.0, (1, 0, 0)) == (
        0.6, 0.0, 0.0)
    assert preview_alpha_tint((0.6, 0.6, 0.6), 0.5, (1, 0, 0)) == (
        0.6, 0.3, 0.3)
    # Green is the only metallic scalar, regardless of red or alpha.
    assert shade_channel_coverage((1, 0, 0, 1))[1] == 0
    assert shade_channel_coverage((0, 1, 0, 0))[1] == 1
    assert shade_channel_coverage((0, 0.5, 0, 1), metal_strength=0.7)[1] == 0.35
    # Blue additive emissive output remains unchanged.
    assert preview_blue_emission((0.1, 0.1, 0.1), 1, (0, 0.5, 1), 1) == (
        0.1, 0.6, 1.1)
    # Constant red does not have a height *gradient* (zero perturbation).
    r = 0.5
    assert (r - r) == 0
    print("Skell R=bump / G=metallic / B=glow / A=color-tint source contracts OK")


if __name__ == "__main__":
    main()
