#!/usr/bin/env python3
"""Build XBX2Unity distributables from the UPM repository root.

Outputs:
  dist/XBX2Unity-UPM-vVERSION.zip
  dist/XBX2Unity-vVERSION.unitypackage
Uses Python standard library; no Unity Editor or game files are required.
"""
from __future__ import annotations

import io
import json
import pathlib
import tarfile
import uuid
import zipfile

ROOT = pathlib.Path(__file__).resolve().parents[1]
DIST = ROOT / "dist"
VERSION = json.loads((ROOT / "package.json").read_text(encoding="utf-8"))["version"]
PREFIX = "Assets/Plugins/XBX2Unity"
GUID_NAMESPACE = uuid.UUID("e12c023c-777b-4685-98d4-3cd6db168cc0")
INCLUDE = (
    "package.json", "package.json.meta", "README.md", "README.md.meta",
    "LICENSE.md", "LICENSE.md.meta", "CHANGELOG.md", "CHANGELOG.md.meta",
    "tools.meta", "tools/build_unitypackage.py", "tools/build_unitypackage.py.meta",
    "Editor.meta",
    "Editor/XBX2UnityMapWindow.cs", "Editor/XBX2UnityMapWindow.cs.meta",
    "Editor/XBX2UnityModelWindow.cs", "Editor/XBX2UnityModelWindow.cs.meta",
    "Editor/XBXCamdoNativeImporter.cs", "Editor/XBXCamdoNativeImporter.cs.meta",
    "Editor/XBXCamdoNativeImporterInspector.cs", "Editor/XBXCamdoNativeImporterInspector.cs.meta",
    "Editor/XBXCamdoHeaderAudit.cs", "Editor/XBXCamdoHeaderAudit.cs.meta",
    "Editor/XBXCamdoBlendAudit.cs", "Editor/XBXCamdoBlendAudit.cs.meta",
    "Editor/XBXSkellHktRig.cs", "Editor/XBXSkellHktRig.cs.meta",
    "Editor/XBXSkellResourceAudit.cs", "Editor/XBXSkellResourceAudit.cs.meta",
    "Editor/XBXSkellSarAnimationAudit.cs", "Editor/XBXSkellSarAnimationAudit.cs.meta",
    "Editor/XBXSkellHktMotionAudit.cs", "Editor/XBXSkellHktMotionAudit.cs.meta",
    "Editor/XBXSkellHktSplineDecoder.cs", "Editor/XBXSkellHktSplineDecoder.cs.meta",
    "Editor/XBXSkellHktClipExporter.cs", "Editor/XBXSkellHktClipExporter.cs.meta",
    "Editor/XBXSkellNativeMotionImporters.cs", "Editor/XBXSkellNativeMotionImporters.cs.meta",
    "Editor/XBXSkellNativeMotionImporterInspectors.cs", "Editor/XBXSkellNativeMotionImporterInspectors.cs.meta",
    "Editor/XBXSkellAnimationBindingDiagnostics.cs", "Editor/XBXSkellAnimationBindingDiagnostics.cs.meta",
    "Editor/XBXSkellSkinning.cs", "Editor/XBXSkellSkinning.cs.meta",
    "Editor/XBXSkellWeightAudit.cs", "Editor/XBXSkellWeightAudit.cs.meta",
    "Editor/XBXSkellMtxt.cs", "Editor/XBXSkellMtxt.cs.meta",
    "Editor/XBXMaterialManifest.cs", "Editor/XBXMaterialManifest.cs.meta",
    "Editor/XBXMaterialInspectorUI.cs", "Editor/XBXMaterialInspectorUI.cs.meta",
    "Editor/XBXSkellMaterialLayers.cs", "Editor/XBXSkellMaterialLayers.cs.meta",
    "Editor/XBXSkellPaintChannelAudit.cs", "Editor/XBXSkellPaintChannelAudit.cs.meta",
    "Editor/XBX2UnityCPKWindow.cs", "Editor/XBX2UnityCPKWindow.cs.meta",
    "Editor/MusafirEx.XBX2Unity.Editor.asmdef",
    "Editor/MusafirEx.XBX2Unity.Editor.asmdef.meta",
    "Runtime.meta", "Runtime/Shaders.meta",
    "Runtime/Shaders/XBXMapVertexBlend.shader",
    "Runtime/Shaders/XBXMapVertexBlend.shader.meta",
    "Runtime/Shaders/XBXSkellColorDiagnostic.shader",
    "Runtime/Shaders/XBXSkellColorDiagnostic.shader.meta",
    "Runtime/Shaders/XBXSkellLayerPreview.shader",
    "Runtime/Shaders/XBXSkellLayerPreview.shader.meta",
)


def guid(path: str) -> str:
    return uuid.uuid5(GUID_NAMESPACE, path).hex


def meta(path: str, *, folder: bool) -> bytes:
    value = guid(path)
    if folder:
        contents = (
            "fileFormatVersion: 2\n"
            f"guid: {value}\n"
            "folderAsset: yes\n"
            "DefaultImporter:\n"
            "  externalObjects: {}\n"
            "  userData: \n"
            "  assetBundleName: \n"
            "  assetBundleVariant: \n"
        )
    else:
        contents = (
            "fileFormatVersion: 2\n"
            f"guid: {value}\n"
            "DefaultImporter:\n"
            "  externalObjects: {}\n"
            "  userData: \n"
            "  assetBundleName: \n"
            "  assetBundleVariant: \n"
        )
    return contents.encode("utf-8")


def add_member(tar: tarfile.TarFile, name: str, content: bytes) -> None:
    info = tarfile.TarInfo(name)
    info.size = len(content)
    info.mode = 0o644
    tar.addfile(info, io.BytesIO(content))


def main() -> None:
    DIST.mkdir(exist_ok=True)
    for filename in INCLUDE:
        if not (ROOT / filename).is_file():
            raise FileNotFoundError(f"Missing package file: {filename}")

    zip_path = DIST / f"XBX2Unity-UPM-v{VERSION}.zip"
    with zipfile.ZipFile(zip_path, "w", zipfile.ZIP_DEFLATED) as out:
        for name in INCLUDE:
            out.write(ROOT / name, arcname=name)
    print("UPM source ZIP:", zip_path)

    archive = DIST / f"XBX2Unity-v{VERSION}.unitypackage"
    assets = {
        PREFIX: None,
        f"{PREFIX}/Editor": None,
        f"{PREFIX}/Editor/XBX2UnityMapWindow.cs": "Editor/XBX2UnityMapWindow.cs",
        f"{PREFIX}/Editor/XBX2UnityModelWindow.cs": "Editor/XBX2UnityModelWindow.cs",
        f"{PREFIX}/Editor/XBXCamdoNativeImporter.cs": "Editor/XBXCamdoNativeImporter.cs",
        f"{PREFIX}/Editor/XBXCamdoNativeImporterInspector.cs": "Editor/XBXCamdoNativeImporterInspector.cs",
        f"{PREFIX}/Editor/XBXCamdoHeaderAudit.cs": "Editor/XBXCamdoHeaderAudit.cs",
        f"{PREFIX}/Editor/XBXCamdoBlendAudit.cs": "Editor/XBXCamdoBlendAudit.cs",
        f"{PREFIX}/Editor/XBXSkellHktRig.cs": "Editor/XBXSkellHktRig.cs",
        f"{PREFIX}/Editor/XBXSkellResourceAudit.cs": "Editor/XBXSkellResourceAudit.cs",
        f"{PREFIX}/Editor/XBXSkellSarAnimationAudit.cs": "Editor/XBXSkellSarAnimationAudit.cs",
        f"{PREFIX}/Editor/XBXSkellHktMotionAudit.cs": "Editor/XBXSkellHktMotionAudit.cs",
        f"{PREFIX}/Editor/XBXSkellHktSplineDecoder.cs": "Editor/XBXSkellHktSplineDecoder.cs",
        f"{PREFIX}/Editor/XBXSkellHktClipExporter.cs": "Editor/XBXSkellHktClipExporter.cs",
        f"{PREFIX}/Editor/XBXSkellNativeMotionImporters.cs": "Editor/XBXSkellNativeMotionImporters.cs",
        f"{PREFIX}/Editor/XBXSkellNativeMotionImporterInspectors.cs": "Editor/XBXSkellNativeMotionImporterInspectors.cs",
        f"{PREFIX}/Editor/XBXSkellAnimationBindingDiagnostics.cs": "Editor/XBXSkellAnimationBindingDiagnostics.cs",
        f"{PREFIX}/Editor/XBXSkellSkinning.cs": "Editor/XBXSkellSkinning.cs",
        f"{PREFIX}/Editor/XBXSkellWeightAudit.cs": "Editor/XBXSkellWeightAudit.cs",
        f"{PREFIX}/Editor/XBXSkellMtxt.cs": "Editor/XBXSkellMtxt.cs",
        f"{PREFIX}/Editor/XBXMaterialManifest.cs": "Editor/XBXMaterialManifest.cs",
        f"{PREFIX}/Editor/XBXMaterialInspectorUI.cs": "Editor/XBXMaterialInspectorUI.cs",
        f"{PREFIX}/Editor/XBXSkellMaterialLayers.cs": "Editor/XBXSkellMaterialLayers.cs",
        f"{PREFIX}/Editor/XBXSkellPaintChannelAudit.cs": "Editor/XBXSkellPaintChannelAudit.cs",
        f"{PREFIX}/Editor/XBX2UnityCPKWindow.cs": "Editor/XBX2UnityCPKWindow.cs",
        f"{PREFIX}/Editor/MusafirEx.XBX2Unity.Editor.asmdef": "Editor/MusafirEx.XBX2Unity.Editor.asmdef",
        f"{PREFIX}/Runtime": None,
        f"{PREFIX}/Runtime/Shaders": None,
        f"{PREFIX}/Runtime/Shaders/XBXMapVertexBlend.shader": "Runtime/Shaders/XBXMapVertexBlend.shader",
        f"{PREFIX}/Runtime/Shaders/XBXSkellColorDiagnostic.shader": "Runtime/Shaders/XBXSkellColorDiagnostic.shader",
        f"{PREFIX}/Runtime/Shaders/XBXSkellLayerPreview.shader": "Runtime/Shaders/XBXSkellLayerPreview.shader",
        f"{PREFIX}/README.md": "README.md",
        f"{PREFIX}/LICENSE.md": "LICENSE.md",
        f"{PREFIX}/CHANGELOG.md": "CHANGELOG.md",
    }
    with tarfile.open(archive, "w:gz") as tar:
        for dest, src in assets.items():
            is_folder = src is None
            if src:
                contents = (ROOT / src).read_bytes()
                corresponding_meta = ROOT / (src + ".meta")
                source_meta = corresponding_meta.read_bytes() if corresponding_meta.exists() else meta(dest, folder=False)
            else:
                contents = b""
                relative = dest[len(PREFIX):].lstrip("/")
                folder_meta = ROOT / (relative + ".meta")
                source_meta = folder_meta.read_bytes() if folder_meta.is_file() else meta(dest, folder=True)
            # Existing .meta GUIDs are stable across UPM and manual .unitypackage.
            import re
            m = re.search(rb"(?m)^guid:\s*([0-9a-f]{32})\s*$", source_meta)
            if not m:
                raise ValueError(f"Missing valid GUID for {dest}")
            id_value = m.group(1).decode("ascii")
            add_member(tar, f"{id_value}/pathname", (dest + "\n").encode("utf-8"))
            add_member(tar, f"{id_value}/asset", contents)
            add_member(tar, f"{id_value}/asset.meta", source_meta)
    with tarfile.open(archive, "r:gz") as tar:
        names = tar.getnames()
        assert len(names) == len(assets) * 3, "Incomplete unitypackage tar"
        assert all(name.split("/")[1] in ("pathname", "asset", "asset.meta") for name in names)
    print("Unity package:", archive)


if __name__ == "__main__":
    main()
