#!/usr/bin/env python3
"""Regression contract for linear sampling of Skell _RGB companion masks.

Unity compiles the importer and shader; this test verifies that source color
and mask-data paths remain distinct in both packed and streamed MTXT records.
"""
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
decoder = (ROOT / "Editor/XBXSkellMtxt.cs").read_text(encoding="utf-8")
importer = (ROOT / "Editor/XBXCamdoNativeImporter.cs").read_text(encoding="utf-8")
shader = (ROOT / "Runtime/Shaders/XBXSkellLayerPreview.shader").read_text(encoding="utf-8")


def srgb_to_linear(c):
    return c / 12.92 if c <= 0.04045 else ((c + 0.055) / 1.055) ** 2.4


def main():
    # A source channel value of 0.5 samples near 0.214 if treated as sRGB.
    # Mask thresholds/gains must use raw 0.5, not an sRGB-decoded value.
    assert 0.21 < srgb_to_linear(0.5) < 0.22
    assert abs(srgb_to_linear(0.5) - 0.5) > 0.25

    assert "internal Texture2D texture;" in decoder
    assert "internal Texture2D linearMaskTexture;" in decoder
    assert "format == 49 && !string.IsNullOrEmpty(sourceName)" in decoder
    assert 'sourceName.EndsWith("_RGB", StringComparison.OrdinalIgnoreCase)' in decoder
    assert "TextureFormat.DXT1, false, true" in decoder
    assert "linearMask.LoadRawTextureData(raw);" in decoder
    assert "linearMask.Apply(false, true);" in decoder
    assert "sourceName=sourceName, texture=tex," in decoder
    assert "linearMaskTexture=linearMask" in decoder

    # Source name must be available before DecodeOne, including streamed pools.
    assert "string sourceName = ReadName(camdo, packed" in decoder
    assert "string sourceName = ReadName(header, pool" in decoder
    assert '"packed", i, sourceName, warnings);' in decoder
    assert 'texId, sourceName, warnings);' in decoder
    assert "DestroyImmediate(old.linearMaskTexture)" in decoder

    assert '[ScriptedImporter(15, "camdo")]' in importer
    assert 'ctx.AddObjectToAsset("mtxt_" + pair.Key.ToString("D4")' in importer
    assert 'ctx.AddObjectToAsset("mtxt_mask_linear_" + pair.Key.ToString("D4")' in importer
    assert "rgbCandidate.texture.linearMaskTexture ??" in importer
    assert "rgbCandidate.texture.texture);" in importer
    assert 'mat.SetTexture("_MainTex", selected.texture);' in importer
    assert 'renderer.sharedMaterial = overrideMaterial;' in importer

    # A linear _RGB mask carries three independent observed roles; the shader
    # must not treat red as gamma-corrected color or alpha as smoothness.
    assert "maskCandidate.r * _MetallicMaskGain" in shader
    assert "maskCandidate.g * _BodyMaskGain" in shader
    assert "maskCandidate.b * _GlowMaskGain" in shader
    assert "metalCoverage * saturate(_MetallicStrength)" in shader
    assert "_MetallicSmoothness" in shader
    assert "maskCandidate.a * _Metallic" not in shader
    assert "_UseObservedRedMetallic < 0.5" in shader
    print("Linear _RGB mask data and opt-in R metallic/G body/B glow contract passed.")


if __name__ == "__main__":
    main()
