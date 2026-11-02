#!/usr/bin/env python3
"""Source-contract and packaging checks for XBX2Unity Material remapping.

This checks that material changes follow the EMD/BRRES/W3X style without
rewriting XBX-specific MTXT, HKT, skinning or texture heuristics.
Not a substitute for compiling the Editor code inside Unity.
"""
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
IMPORTER = (ROOT / "Editor/XBXCamdoNativeImporter.cs").read_text(encoding="utf8")
INSPECTOR = (ROOT / "Editor/XBXCamdoNativeImporterInspector.cs").read_text(encoding="utf8")
MANIFEST = (ROOT / "Editor/XBXMaterialManifest.cs").read_text(encoding="utf8")
UI = (ROOT / "Editor/XBXMaterialInspectorUI.cs").read_text(encoding="utf8")
BUILDER = (ROOT / "tools/build_unitypackage.py").read_text(encoding="utf8")

def check(src, *tokens):
    for token in tokens:
        assert token in src, f"Missing required material contract: {token}"

check(IMPORTER,
    "[ScriptedImporter(13, \"camdo\")]",
    "public Shader generatedMaterialShaderOverride;",
    "public List<XBXMaterialOverride> materialOverrides",
    "ScriptableObject.CreateInstance<XBXMaterialManifest>()",
    'ctx.AddObjectToAsset("xbx_material_manifest", manifest)',
    'ctx.DependsOnSourceAsset(sourcePath);',
    "new Material(shader)",
    "manifest.entries[sourceIndex]",
    "externalMaterials.TryGetValue(sourceIndex, out var overrideMaterial)",
    "renderer.sharedMaterial = overrideMaterial;",
    "XBXSkellSkinning.TryBuild",
    "XBXSkellMtxt.Decode",
    "candidate.ScoreAsAlbedo(preferSkellRgbCompanionAsAlbedo)",
    "FindPipelineShader()",
)
check(INSPECTOR, "XBXMaterialInspectorUI.Draw")
check(MANIFEST,
    "public sealed class XBXMaterialManifest : ScriptableObject",
    "public int materialIndex;",
    "public Material generatedMaterial;",
    "public Material assignedMaterial;",
    "public int textureId;",
    "public string sourceName;",
    "public Texture2D decodedTexture;",
)
check(UI,
    "serialized.FindProperty(\"materialOverrides\")",
    "AssetDatabase.GenerateUniqueAssetPath",
    "AssetDatabase.CreateAsset(clone, path)",
    "new Material(entry.generatedMaterial)",
    "SetOverride(list, entry.materialIndex, clone)",
    "importer.SaveAndReimport()",
    "GUIUtility.ExitGUI()",
    "XBXMaterialManifest manifest",
)
for path in ("XBXMaterialManifest.cs", "XBXMaterialInspectorUI.cs"):
    check(BUILDER, f'"Editor/{path}"', f'"Editor/{path}.meta"')
    assert (ROOT / "Editor" / path).exists()
    assert (ROOT / "Editor" / (path+".meta")).exists()

# Source data never overwritten by external .mat extraction.
assert "File.WriteAllBytes(" not in UI and "File.WriteAllBytes(" not in IMPORTER
print("XBX material workflow contract checks passed; Unity compile/preview still required.")
