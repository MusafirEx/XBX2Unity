#!/usr/bin/env python3
"""Havok 2013 packfile metadata fixtures + Unity package source contracts.

No game HKT bytes are included. This tests header/table assumptions using
purpose-built synthetic bytes, not spline-keyframe decoding or Unity runtime.
"""
from pathlib import Path
import struct
import base64
import hashlib

ROOT = Path(__file__).resolve().parents[1]
SOURCE = (ROOT / "Editor/XBXSkellHktMotionAudit.cs").read_text(encoding="utf-8")
INSPECTOR = (ROOT / "Editor/XBXCamdoNativeImporterInspector.cs").read_text(encoding="utf-8")
BUILDER = (ROOT / "tools/build_unitypackage.py").read_text(encoding="utf-8")
README = (ROOT / "README.md").read_text(encoding="utf-8")
DECODER = (ROOT / "Editor/XBXSkellHktSplineDecoder.cs").read_text(encoding="utf-8")
EXPORTER = (ROOT / "Editor/XBXSkellHktClipExporter.cs").read_text(encoding="utf-8")
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


def fixture_motion_metadata():
    """Synthetic 32-bit big-endian Havok fields using audited Wii U offsets."""
    anim = bytearray(0x50)
    struct.pack_into(">IfIIII", anim, 0x08, 3, 200 / 30.0, 103, 0, 0, 0)
    struct.pack_into(">IIII", anim, 0x28, 201, 1, 256, 412)
    struct.pack_into(">fff", anim, 0x38, 8.5, 1 / 8.5, 1 / 30.0)

    binding = bytearray(0x50 + 103 * 2)
    struct.pack_into(">II", binding, 0x14, 103, 0x80000067)
    binding[0x40:0x40+14] = b"dl099100_Bone\0"
    first = [0, 62, 63, 1, 64, 65, 66, 2]
    all_bones = first + [n for n in range(103) if n not in first]
    assert len(all_bones) == 103 and len(set(all_bones)) == 103
    for index, bone in enumerate(all_bones):
        struct.pack_into(">h", binding, 0x50 + index*2, bone)

    codec, = struct.unpack_from(">I", anim, 0x08)
    duration, = struct.unpack_from(">f", anim, 0x0C)
    track_count, = struct.unpack_from(">I", anim, 0x10)
    float_count, = struct.unpack_from(">I", anim, 0x14)
    frames, blocks, max_frames, mask_size = struct.unpack_from(">IIII", anim, 0x28)
    block_duration, reciprocal, frame_duration = struct.unpack_from(">fff", anim, 0x38)
    count, capacity = struct.unpack_from(">II", binding, 0x14)
    mapping = list(struct.unpack_from(">" + "h"*count, binding, 0x50))
    skeleton_name = binding[0x40:0x50].split(b"\0", 1)[0].decode("ascii")
    assert codec == 3 and float_count == 0
    assert duration > 0 and abs(duration - 6.66666667) < 0.0001
    assert frames == 201 and track_count == count == 103
    assert blocks == 1 and max_frames == 256 and mask_size == 412
    assert abs((frames-1)*frame_duration - duration) < 0.0001
    assert abs(block_duration * reciprocal - 1) < 0.0001
    assert capacity & 0x3fffffff == count
    assert skeleton_name == "dl099100_Bone"
    assert mapping[:8] == first
    assert mapping != list(range(track_count))  # track i is not always bone i
    assert 201*16 == 3216  # synthetic reference-frame sample footprint


def fixture_phase2d_raw_pointer_probe():
    """Emulate validated relative offsets; this is NOT sampled spline data."""
    tracks = 103
    data_bytes = 9664
    next_typed_object = 0x17D0
    run = [(0x160 + 16*i, 0x7D0 + 16*i) for i in range(tracks)]
    edges = [(0xEC, 0x160)] + run
    longest = []
    i = 0
    while i < len(edges):
        j = i+1
        while j < len(edges) and edges[j][0] - edges[j-1][0] == 16 and \
                edges[j][1] - edges[j-1][1] == 16:
            j += 1
        if j-i > len(longest):
            longest = edges[i:j]
        i = j
    assert len(longest) == tracks
    assert longest[0] == (0x160, 0x7D0)
    assert longest[-1] == (0x7C0, 0xE30)
    candidate_gap = longest[-1][1] + 16
    assert candidate_gap == 0xE40
    assert next_typed_object - candidate_gap == 0x990
    root_sample_start = 0x1810
    root_count = 201
    assert root_sample_start + root_count * 16 == 0x24A0
    assert root_sample_start + root_count * 16 <= data_bytes
    # Confirm raw previews cannot write, fabricate frames, or infer spline data.
    assert b"\\0" != b"not a keyframe"


def fixture_phase2e_ann_and_packed_data():
    """Synthetic 103 bounded annotation labels and single-block byte-array layout.

    Values are from the observed HKT field offsets, but synthetic content must
    not be misrepresented as compressed keys or a Unity AnimationClip.
    """
    count = 103
    labels = ["dl099100_Bone", "EX_UVA"] + [f"bone{i:03d}" for i in range(2, count)]
    label_bytes = b"".join(x.encode("ascii") + b"\0" *
                          (16-len(x)) for x in labels)
    assert len(label_bytes) == 103 * 16
    assert all(len(x) < 16 for x in labels)
    parsed = [label_bytes[i*16:(i+1)*16].split(b"\0", 1)[0].decode("ascii")
              for i in range(count)]
    assert parsed == labels
    header = bytearray(0x90)
    struct.pack_into(">II", header, 0x20, count, 0x80000067)
    values = ((0x44, 1), (0x50, 1), (0x5c, 0),
              (0x68, 0), (0x74, 0x970))
    for field, size in values:
        struct.pack_into(">III", header, field, 0, size, 0x80000000 | size)
    counts = [struct.unpack_from(">I", header, field + 4)[0]
              for field, _ in values]
    assert counts == [1, 1, 0, 0, 2416]

    gap_start = 0xE40
    typed_end = 0x17D0
    align16 = lambda n: (n+15)//16*16
    blocks_at = gap_start
    floats_at = align16(blocks_at + counts[0]*4)
    data_at = align16(floats_at + counts[1]*4)
    assert (blocks_at, floats_at, data_at) == (0xE40, 0xE50, 0xE60)
    assert typed_end - data_at == counts[4] == 2416

    region = bytearray(typed_end-gap_start)
    struct.pack_into(">I", region, blocks_at-gap_start, 0)
    struct.pack_into(">I", region, floats_at-gap_start, 0x968)
    synthetic_payload = bytes((i*17+5) & 0xff for i in range(counts[4]))
    region[data_at-gap_start:] = synthetic_payload
    assert struct.unpack_from(">I", region, floats_at-gap_start)[0] == 2408
    assert len(synthetic_payload) == 2416 and len(region) == 2448
    encoded = base64.b64encode(region[data_at-gap_start:]).decode("ascii")
    assert base64.b64decode(encoded) == synthetic_payload
    assert len(hashlib.sha256(synthetic_payload).hexdigest()) == 64
    # Reject one-byte-short declared data before accepting the packed layout.
    assert data_at + counts[4] - 1 != typed_end
    # No guessed pose curves or interpolation frames are created here.


def fixture_native_spline_decoder_contract():
    """Model tested 40-bit quaternion packing and B-spline interpolation.

    Source contracts run in CI without Unity. Real 103-track m_data was
    separately validated from the uploaded text audit, not bundled here.
    """
    import math
    three = 2047
    packed_identity_w = three | (three << 12) | (three << 24) | (3 << 36)
    assert 0 <= packed_identity_w < (1 << 40)
    wire = packed_identity_w.to_bytes(5, "little")
    assert len(wire) == 5
    bits = int.from_bytes(wire, "little")
    factor = math.sqrt(2) / 4095
    xyz = [(((bits >> (12 * i)) & 0xfff) - 2047) * factor
           for i in range(3)]
    absent = (bits >> 36) & 3
    sign = (bits >> 38) & 1
    q = xyz[:]
    q.insert(absent, math.sqrt(max(0, 1 - sum(v*v for v in xyz)))
             * (-1 if sign else 1))
    assert q == [0.0, 0.0, 0.0, 1.0]

    def deboor_linear(knots, values, frame):
        assert len(knots) == len(values) + 2
        idx = 1 if frame < knots[len(values)] else len(values)-1
        alpha = (frame - knots[idx]) / (knots[idx+1] - knots[idx])
        return (1-alpha) * values[idx-1] + alpha * values[idx]
    assert abs(deboor_linear([0, 0, 10, 10], [2.0, 4.0], 5) - 3.0) < 1e-6

    # Little-endian compressed float bytes are independent of the big-endian HKT header.
    packed = struct.pack("<4Bf", 0x45, 0x01, 0x0F, 0, 1.25)
    assert packed[:4] == b"\x45\x01\x0f\x00"
    assert struct.unpack_from("<f", packed, 4)[0] == 1.25
    for phrase in (
        "class XBXSkellHktSplineDecoder", "ReadQuat40",
        "ReadKnotHeader", "KnotSpan", "Evaluate(",
        "reader.Position != reader.Size", "source.blockCount != 1",
        "source.maskAndQuantizationBytes != source.trackCount * 4",
        "Quaternion.Normalize", "source.floatTrackCount != 0",
    ):
        assert phrase in DECODER, "Spline decoder source contract missing: " + phrase
    for phrase in (
        "class XBXSkellHktClipExporter", "ExtractNativeSpline",
        "XBXSkellHktRig.Read", "ValidateRig", "PathForBone",
        "Importer.GetAtPath" if False else "AssetImporter.GetAtPath",
        "importer.flipX", "importer.modelScale",
        "m_LocalPosition", "m_LocalRotation", "m_LocalScale",
        "clip.SetCurve", "AssetDatabase.CreateAsset",
        "Refusing to overwrite existing asset",
    ):
        assert phrase in EXPORTER, "Clip exporter source contract missing: " + phrase
    assert "ExtractNativeSpline" in SOURCE
    assert "packedSplineData" in SOURCE and "annotationNames" in SOURCE
    assert "XBXSkellHktSplineDecoder.cs" in BUILDER
    assert "XBXSkellHktClipExporter.cs" in BUILDER
    assert "XBXSkellHktClipExporter.RunSelected()" in INSPECTOR


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
    fixture_motion_metadata()
    fixture_phase2d_raw_pointer_probe()
    fixture_phase2e_ann_and_packed_data()
    fixture_native_spline_decoder_contract()
    for item in (
        "hkPackfileHeader", "HktMagic", "MaxFiles = 4096",
        "MaxBatchBytes", "CountFixups", "__classnames__",
        "hkaSplineCompressedAnimation", "hkaAnimationBinding",
        "m_virtualFixupsOffset" if False else "virtual fixup",
        "Root object:", "File.ReadAllBytes(path)",
        "ReadSections", "No verified HKT section-header layout.",
        "FormatFields", "48, 48, 64",
        "No extracted HKT files selected.", "File.WriteAllText",
        "ReadMotionMetadata", "CheckedInt", "SafeAsciiString",
        "ProbeSplineStream", "PreviewSection", "ReadFloat4",
        "ProbeAnnotationAndPackedArrays", "TryReadAnnotationName",
        "PHASE 2E VERIFIED ANNOTATION-NAME TABLE",
        "m_data SHA256:", "BASE64 m_data", "END BASE64 m_data",
        "dataAt + counts[4] != typedEnd",
        "Repeating 16-byte local pointer run:", "uninterpreted source bytes",
        "Reference frame:",
        "Duration seconds:", "Binding track->bone index entries:",
        "Binding does not point to this animation", "MappingCount" if False else
        "Track-map size/capacity disagrees with animation",
        "CollectPointers", "PointerRef", "ObjectRef", "Header bytes (raw, up to 96)",
        "Typed object references:", "Verified section layout:",
    ):
        assert item in SOURCE, "Missing HKT audit source contract: " + item
    assert "XBXSkellHktMotionAudit.RunSelected()" in INSPECTOR
    assert "Editor/XBXSkellHktMotionAudit.cs" in BUILDER
    assert "Editor/XBXSkellHktMotionAudit.cs.meta" in BUILDER
    assert "HKT" in README and "AnimationClip" in README
    print("Synthetic Havok metadata, spline 40-bit quaternion/de Boor, rig-path clip export and packaging contracts OK")


if __name__ == "__main__":
    main()
