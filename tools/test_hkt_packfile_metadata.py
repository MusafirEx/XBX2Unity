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


def fixture(big_endian=True, stride=48, first_field=16):
    endian = ">" if big_endian else "<"
    names = bytearray()
    offsets = []
    for name in CLASSES:
        offsets.append(len(names))
        names += name.encode("ascii") + b"\0"
    names += bytes(align16(len(names)) - len(names))
    classes_start = 0x40 + 3 * stride
    types_start = classes_start + len(names)
    data_start = types_start
    local = 64
    virtual_count = len(CLASSES)
    local_end = local + 2 * 8
    global_end = local_end + 12
    data_end = global_end + virtual_count * 12
    blob = bytearray(data_start + data_end)
    blob[:8] = MAGIC
    blob[0x10:0x14] = bytes([4, 0 if big_endian else 1, 1, 1])
    struct.pack_into(endian + "IIII", blob, 0x14, 3, 2, 0, 0)
    blob[0x28:0x28+len(b"hk_2013.1.0-r1")] = b"hk_2013.1.0-r1"

    def section(index, label, base, fixes, data_end_offset):
        at = 0x40 + index * stride
        blob[at:at+len(label)] = label.encode("ascii")
        if first_field == 16:
            struct.pack_into(endian + "8I", blob, at + 16, base,
                             *fixes, data_end_offset, 0)
        else:
            blob[at+16:at+20] = b"\xff" * 4
            struct.pack_into(endian + "7I", blob, at + 20, base,
                             *fixes, data_end_offset)

    # fixes: local, global, virtual, exports, imports
    section(0, "__classnames__", classes_start,
            (len(names),) * 5, len(names))
    section(1, "__types__", types_start, (0,) * 5, 0)
    section(2, "__data__", data_start,
            (local, local_end, global_end, data_end, data_end), data_end)
    blob[classes_start:classes_start + len(names)] = names
    struct.pack_into(endian + "4I", blob, data_start + local, 4, 8, 24, 40)
    struct.pack_into(endian + "3I", blob, data_start + local_end, 32, 2, 8)
    for i, off in enumerate(offsets):
        struct.pack_into(endian + "III", blob, data_start + global_end + 12*i,
                         8 * i, 0, off)
    return bytes(blob)


def parse_fixture(data, stride=48, first_field=16):
    assert data[:8] == MAGIC
    endian = ">" if data[0x11] == 0 else "<"
    count, root_section, root_offset = struct.unpack_from(endian + "III", data, 0x14)
    assert count == 3 and root_section == 2 and root_offset == 0
    sections = []
    for i in range(count):
        at = 0x40 + i*stride
        label = data[at:at+16].split(b"\0", 1)[0].decode("ascii")
        fields = struct.unpack_from(endian + ("8I" if first_field == 16 else "7I"),
                                    data, at + first_field)
        base, local, global_, virtual, exports, imports, end = fields[:7]
        assert local <= global_ <= virtual <= exports <= imports <= end
        assert base + end <= len(data)
        sections.append((label, base, local, global_, virtual, exports))
    assert [s[0] for s in sections] == ["__classnames__", "__types__", "__data__"]
    _, base, local, global_, virtual, exports = sections[2]
    local_ptrs = [
        struct.unpack_from(endian + "II", data, at)
        for at in range(base + local, base + global_, 8)
    ]
    global_ptrs = [
        struct.unpack_from(endian + "III", data, at)
        for at in range(base + global_, base + virtual, 12)
    ]
    assert local_ptrs == [(4, 8), (24, 40)]
    assert global_ptrs == [(32, 2, 8)]
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
        for stride, first_field in ((48, 16), (48, 20), (64, 20)):
            sample = fixture(big, stride=stride, first_field=first_field)
            assert [name for _, name in parse_fixture(sample, stride, first_field)] == list(CLASSES)
            bad = bytearray(sample)
            endian = ">" if big else "<"
            at = 0x40 + 2*stride + first_field + 24
            struct.pack_into(endian + "I", bad, at, len(sample) + 100)
            try:
                parse_fixture(bytes(bad), stride, first_field)
            except AssertionError:
                pass
            else:
                raise AssertionError("HKT section end corruption was not rejected")
    for item in (
        "hkPackfileHeader", "HktMagic", "MaxFiles = 4096",
        "MaxBatchBytes", "CountFixups", "__classnames__",
        "hkaSplineCompressedAnimation", "hkaAnimationBinding",
        "m_virtualFixupsOffset" if False else "virtual fixup",
        "Root object:", "File.ReadAllBytes(path)",
        "ReadSections", "No verified HKT section-header layout.",
        "FormatFields", "48, 48, 64",
        "No extracted HKT files selected.", "File.WriteAllText",
        "CollectPointers", "PointerRef", "ObjectRef", "Header bytes (raw, up to 96)",
        "Typed object references:", "Verified section layout:",
    ):
        assert item in SOURCE, "Missing HKT audit source contract: " + item
    assert "XBXSkellHktMotionAudit.RunSelected()" in INSPECTOR
    assert "Editor/XBXSkellHktMotionAudit.cs" in BUILDER
    assert "Editor/XBXSkellHktMotionAudit.cs.meta" in BUILDER
    assert "HKT" in README and "AnimationClip" in README
    print("Synthetic HKT 48/16, 48/20, 64/20 section layouts, typed object references, local/global pointers and bounds OK")


if __name__ == "__main__":
    main()
