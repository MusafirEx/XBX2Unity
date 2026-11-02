#!/usr/bin/env python3
"""Skell v0.9.2 BC1 RGB companion audit source checks.

Uses known DXT1 golden blocks to guard channel-stat terminology.
The native C# source is checked for the critical pipeline contracts;
actual Unity C# / HLSL compilation requires a local Unity editor.
"""
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
AUDIT = (ROOT / "Editor/XBXSkellPaintChannelAudit.cs").read_text()
DECODER = (ROOT / "Editor/XBXSkellMtxt.cs").read_text()
MANIFEST = (ROOT / "Editor/XBXMaterialManifest.cs").read_text()
IMPORTER = (ROOT / "Editor/XBXCamdoNativeImporter.cs").read_text()
INSPECTOR = (ROOT / "Editor/XBXCamdoNativeImporterInspector.cs").read_text()
MATUI = (ROOT / "Editor/XBXMaterialInspectorUI.cs").read_text()
SHADER = (ROOT / "Runtime/Shaders/XBXSkellLayerPreview.shader").read_text()
BUILDER = (ROOT / "tools/build_unitypackage.py").read_text()


def unpack565(c):
    return (((c >> 11 & 31) * 255 + 15) // 31,
            ((c >> 5 & 63) * 255 + 31) // 63,
            ((c & 31) * 255 + 15) // 31)


def decode_block(c0, c1, index_bits):
    a, b = unpack565(c0), unpack565(c1)
    colors = [(*a, 255), (*b, 255)]
    if c0 > c1:
        colors.extend(
            [(*((2 * a[i] + b[i]) // 3 for i in range(3)), 255),
             (*((a[i] + 2 * b[i]) // 3 for i in range(3)), 255)]
        )
    else:
        colors.extend(
            [(*((a[i] + b[i]) // 2 for i in range(3)), 255), (0, 0, 0, 0)]
        )
    return [colors[(index_bits >> (2 * i)) & 3] for i in range(16)]


def main():
    # BC1 4-color: red/blue + 2 interpolation colors, full alpha.
    red_blue = decode_block(0xF800, 0x001F, 0xE4E4E4E4)
    assert red_blue[0] == (255, 0, 0, 255)
    assert red_blue[1] == (0, 0, 255, 255)
    assert red_blue[2] == (170, 0, 85, 255)
    assert red_blue[3] == (85, 0, 170, 255)
    # BC1 3-color mode: c0 <= c1: index 3 is FULLY transparent.
    transparent = decode_block(0x0000, 0xFFFF, 0xFFFFFFFF)
    assert all(pixel[3] == 0 for pixel in transparent)
    assert all(pixel[3] in (0, 255) for pixel in red_blue + transparent)

    assert "public bool skellUseRgbCompanionAsPaintMask = false;" in IMPORTER
    assert "public float skellPaintStrength = 0f;" in IMPORTER
    assert '[ScriptedImporter(13, "camdo")]' in IMPORTER
    assert "XBXSkellPaintChannelAudit.ProfileBc1(raw,width,height)" in DECODER
    assert DECODER.index("ProfileBc1(raw,width,height)") < DECODER.index(
        "tex.Apply(false, true);")
    assert "sourceTexture.bc1ChannelProfile = decoded.bc1ChannelProfile;" in IMPORTER
    assert "public string bc1ChannelProfile;" in MANIFEST
    assert "Export RGB Companion Channel Audit" in MATUI
    assert "File.WriteAllText(destination, lines.ToString())" in MATUI
    assert "skellMaskChannel" in INSPECTOR and "_MaskChannel" in SHADER
    assert "if (hasCandidate)" in IMPORTER
    assert 'mat.SetTexture("_PaintMask", rgbCandidate.texture.texture);' in IMPORTER
    assert "skellUseRgbCompanionAsPaintMask ? 1f : 0f" in IMPORTER
    assert "float line =" not in SHADER  # avoid previous HLSL syntax regression
    assert "_ViewMode > 9.5" in SHADER
    assert "Use Skell Layer Preview Shader" in INSPECTOR
    assert "Editor/XBXSkellPaintChannelAudit.cs" in BUILDER
    assert "Editor/XBXSkellPaintChannelAudit.cs.meta" in BUILDER

    print("BC1 channel goldens, profile wiring, diagnostic-only _RGB binding OK.")


if __name__ == "__main__":
    main()
