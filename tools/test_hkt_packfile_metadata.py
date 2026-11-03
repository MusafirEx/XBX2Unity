#!/usr/bin/env python3
"""Havok 2013 packfile metadata fixtures + Unity package source contracts.

No game HKT bytes are included. This tests header/table assumptions using
purpose-built synthetic bytes, not spline-keyframe decoding or Unity runtime.
"""
from pathlib import Path
import struct

ROOT = Path(__file__).resolve().parents[1]
SOURCE = (ROOT / "Editor/XBXSkellHktMotionAudit.cs").read_text(encoding="utf-8")
INSPECTOR = (ROOT / "Editor/XBXCamdoNativeImporterInspector.cs").read_text(encoding="utf-8")
BUILDER = (ROOT / "tools/build_unitypackage.py").read_text(encoding="utf-8")
README = (ROOT / "README.md").read_text(encoding="utf-8")
MAGIC = b"\x57\xe0\xe0\x57\x10\xc0\xc0\x10"
CLASSES = ("hkRootLevelContainer", "hkaSplineCompressedAnimation",
           "hkaAnimationBinding", "hkaAnimationContainer")


def align16(n):
    return (n + 15) // 16 * 16


def fixture(big_endian=True):
    endian = ">" if big_endian else "<"
    names = bytearray()
    offsets = []
    for name in CLASSES:
        offsets.append(len(names))
        names += name.encode("ascii") + b"\0"
    names += bytes(align16(len(names)) - len(names))
    classes_start = 0xD0
    types_start = classes_start + len(names)
    data_start = types_start
    local = 64
    virtual_count = len(CLASSES)
    data_end = local + virtual_count * 12
    blob = bytearray(data_start + data_end)
    blob[:8] = MAGIC
    blob[0x10:0x14] = bytes([4, 0 if big_endian else 1, 1, 1])
    struct.pack_into(endian + "IIII", blob, 0x14, 3, 2, 0, 0)
    blob[0x28:0x28+len(b"hk_2013.1.0-r1")] = b"hk_2013.1.0-r1"

    def section(index, label, base, fixes, data_end_offset):
        at = 0x40 + index * 48
        blob[at:at+len(label)] = label.encode("ascii")
        struct.pack_into(endian + "8I", blob, at + 16, base,
                         *fixes, data_end_offset, 0)

    # fixes: local, global, virtual, exports, imports
    section(0, "__classnames__", classes_start,
            (len(names),) * 5, len(names))
    section(1, "__types__", types_start, (0,) * 5, 0)
    section(2, "__data__", data_start,
            (local, local, local, data_end, data_end), data_end)
    blob[classes_start:classes_start + len(names)] = names
    for i, off in enumerate(offsets):
        struct.pack_into(endian + "III", blob, data_start + local + 12*i,
                         8 * i, 0, off)
    return bytes(blob)


def parse_fixture(data):
    assert data[:8] == MAGIC
    endian = ">" if data[0x11] == 0 else "<"
    count, root_section, root_offset = struct.unpack_from(endian + "III", data, 0x14)
    assert count == 3 and root_section == 2 and root_offset == 0
    sections = []
    for i in range(count):
        at = 0x40 + i*48
        label = data[at:at+16].split(b"\0", 1)[0].decode("ascii")
        fields = struct.unpack_from(endian + "8I", data, at + 16)
        base, local, global_, virtual, exports, imports, end, _ = fields
        assert local <= global_ <= virtual <= exports <= imports <= end
        assert base + end <= len(data)
        sections.append((label, base, local, virtual, exports))
    assert [s[0] for s in sections] == ["__classnames__", "__types__", "__data__"]
    _, base, local, virtual, exports = sections[2]
    refs = []
    for at in range(base + virtual, base + exports, 12):
        position, class_index, string_off = struct.unpack_from(endian + "III", data, at)
        assert position < local and class_index < len(sections)
        _, string_base, string_end, *_ = sections[class_index]
        assert string_off < string_end
        start = string_base + string_off
        label = data[start:start+100].split(b"\0", 1)[0].decode("ascii")
        refs.append((position, label))
    return refs


def main():
    for big in (True, False):
        sample = fixture(big)
        assert [name for _, name in parse_fixture(sample)] == list(CLASSES)
        bad = bytearray(sample)
        # Invalid data-section end must be rejected.
        endian = ">" if big else "<"
        struct.pack_into(endian + "I", bad, 0xA0 + 16 + 40, len(sample) + 100)
        try:
            parse_fixture(bytes(bad))
        except AssertionError:
            pass
        else:
            raise AssertionError("HKT bounds corruption was not rejected")
    for item in (
        "hkPackfileHeader", "HktMagic", "MaxFiles = 4096",
        "MaxBatchBytes", "CountFixups", "__classnames__",
        "hkaSplineCompressedAnimation", "hkaAnimationBinding",
        "m_virtualFixupsOffset" if False else "virtual fixup",
        "Root object:", "File.ReadAllBytes(path)",
        "No extracted HKT files selected.", "File.WriteAllText",
    ):
        assert item in SOURCE, "Missing HKT audit source contract: " + item
    assert "XBXSkellHktMotionAudit.RunSelected()" in INSPECTOR
    assert "Editor/XBXSkellHktMotionAudit.cs" in BUILDER
    assert "Editor/XBXSkellHktMotionAudit.cs.meta" in BUILDER
    assert "HKT" in README and "AnimationClip" in README
    print("Synthetic Havok 2013 endian headers, fixup metadata, bounds and packaging contracts OK")


if __name__ == "__main__":
    main()
