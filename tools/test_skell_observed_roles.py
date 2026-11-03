#!/usr/bin/env python3
"""Source-contract tests for user-observed dl099110 Skell RGB roles.

The user directly compared against the game:
  Green channel -> body region; Blue -> glowing parts.
No evidence has yet assigned a meaning to Red or BC1 alpha.

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
        "public bool skellUseObservedGreenBody = false;",
        "public bool skellUseObservedBlueGlow = false;",
        'mat.SetFloat("_UseObservedGreenBody"',
        'mat.SetFloat("_UseObservedBlueGlow"',
        "hasCandidate && skellUseObservedGreenBody",
        "hasCandidate && skellUseObservedBlueGlow",
        "renderer.sharedMaterial = overrideMaterial;",
        "XBXSkellSkinning.TryBuild")
    includes(ui,
        "Observed Green - Body Mask (dl099110)",
        "Observed Blue - Glow Mask (dl099110)",
        "Green -> Body Color Tint",
        "Blue -> Glow Emission Preview")
    assert "float line =" not in shader  # HLSL reserved keyword
    assert "skellRedMask" not in imp and "_UseObservedRed" not in shader
    assert simulate((0.6, 0.6, 0.6), (0.3, 0.0, 0.0, 1.0),
                    body=True, glow=True, body_color=(1, 0, 0)) == (0.6, 0.6, 0.6)
    assert simulate((0.6, 0.6, 0.6), (0, 1, 0, 1), body=True,
                    body_color=(1, 0, 0)) == (0.6, 0.0, 0.0)
    assert simulate((0.1, 0.1, 0.1), (0, 0, 1, 1), glow=True,
                    glow_color=(0, 0.5, 1)) == (0.1, 0.6, 1.1)
    assert simulate((0.1, 0.1, 0.1), (0, 0, 1, 1)) == (0.1, 0.1, 0.1)
    print("Skell G=body/B=glow role contract and preview behavior passed.")


if __name__ == "__main__":
    main()
