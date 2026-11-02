#!/usr/bin/env python3
"""Regress Skell color texture SOURCE HEURISTIC (not full shader fidelity).

Confirms that 'DL109100_FL' wins over 'DL109100_FL_RGB' by default.
This test mirrors source score rules and asserts the native C# call
uses preferSkellRgbCompanionAsAlbedo, but does not compile C#.
"""
from pathlib import Path


NEGATIVE_TOKENS = {
    "SPM", "GLO", "GLW", "RFM", "REF", "RF",
    "OCL", "DEP", "NRM", "NM", "NORMAL"
}


def score(name: str, fmt: int, usage: int, prefer_rgb: bool = False) -> int:
    n = name.upper()
    if fmt != 49 or usage in {16, 18, 528, 530, 65569}:
        return -1
    if any(token in NEGATIVE_TOKENS for token in n.split("_")):
        return -1
    if n.endswith("_RGB"):
        return 170 if prefer_rgb else 40
    if n.endswith(("_COL", "_DCL", "_AVA")):
        return 160
    if usage == 80:
        return 150
    if usage in {96, 48}:
        return 140
    if usage in {32, 34, 544}:
        return 100
    return 20


def main():
    root = Path(__file__).resolve().parents[1]
    importer = (root / "Editor/XBXCamdoNativeImporter.cs").read_text()
    mtxt = (root / "Editor/XBXSkellMtxt.cs").read_text()
    assert "public bool preferSkellRgbCompanionAsAlbedo = false;" in importer
    assert "candidate.ScoreAsAlbedo(preferSkellRgbCompanionAsAlbedo)" in importer
    assert "tex.ScoreAsAlbedo(" in importer
    assert 'n.EndsWith("_RGB", StringComparison.Ordinal)' in mtxt
    assert "preferRgbVariant ? 170 : 40" in mtxt
    assert "token == \"RFM\"" in mtxt

    # Exact source names and IDs observed for dl109100 CAMDO in the
    # Unity Editor.log; no claim about unknown original shader semantics.
    fixture = [
        [("DL109100_FL", 49, 32), ("DL109100_FL_RGB", 49, 32), ("DL109100_FL_NRM", 53, 18), ("DL109100_FL_SPM", 49, 16)],
        [("DMG", 49, 32), ("DMG_RGB", 49, 32), ("DMG_NRM", 53, 18)],
        [("DL109100_CP", 49, 32), ("DL109100_CP_RGB", 49, 32)],
        [("DL109100_DM", 49, 32), ("DL109100_DM_NRM", 53, 18)],
        [("DL109100_SKY", 49, 32), ("DL109100_SKY_RGB", 49, 32)],
        [("DL109100", 49, 32), ("DL109100_RGB", 49, 32)],
        [("DL109100", 49, 32), ("DL109100_RF", 49, 32)],
    ]
    expected_primary = [
        "DL109100_FL", "DMG", "DL109100_CP", "DL109100_DM",
        "DL109100_SKY", "DL109100", "DL109100"
    ]
    expected_rgb = [
        "DL109100_FL_RGB", "DMG_RGB", "DL109100_CP_RGB",
        "DL109100_DM", "DL109100_SKY_RGB", "DL109100_RGB",
        "DL109100"
    ]
    for index, choices in enumerate(fixture):
        normal = max(choices, key=lambda v: score(*v))
        rgb = max(choices, key=lambda v: score(*v, prefer_rgb=True))
        assert normal[0] == expected_primary[index], (index, normal)
        assert rgb[0] == expected_rgb[index], (index, rgb)
    # Variant-suffixed non-color should not score as albedo.
    assert score("DL019600_RFM_B", 49, 32) == -1
    assert score("DL019600_NRM_B", 53, 18) == -1
    assert score("DL019600_B", 49, 32) == 100
    print("Skell source texture scoring regression: 7 dl109100 groups OK in both preferences.")


if __name__ == "__main__":
    main()
