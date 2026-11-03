#!/usr/bin/env python3
"""Synthetic 1RAS test fixtures and XBX2Unity source contracts.

This validates the documented archive table interpretation with small,
purpose-built example bytes. It does not claim Havok motion keyframe support,
nor does it run the Unity C# compiler.
"""
from pathlib import Path
import struct

ROOT = Path(__file__).resolve().parents[1]
AUDIT = (ROOT / "Editor/XBXSkellSarAnimationAudit.cs").read_text(encoding="utf-8")
UI = (ROOT / "Editor/XBXCamdoNativeImporterInspector.cs").read_text(encoding="utf-8")
BUILDER = (ROOT / "tools/build_unitypackage.py").read_text(encoding="utf-8")
README = (ROOT / "README.md").read_text(encoding="utf-8")
MAGIC = b"\x57\xe0\xe0\x57\x10\xc0\xc0\x10"


def fixture(big_endian=False, corrupt=False):
    order = ">" if big_endian else "<"
    table = 160
    entry_count = 2
    start = table + entry_count * 64
    hkt = MAGIC + b"hkaSplineCompressedAnimation\0" + bytes(128)
    bc = b"BC\0\0" + bytes(32)
    data = bytearray(start + len(hkt) + len(bc))
    data[0:4] = b"SAR1" if big_endian else b"1RAS"
    struct.pack_into(order + "5I", data, 4, len(data), 0x101 if big_endian else 0x102,
                     entry_count, table, start)
    data[32:32+len(b"dl019600.sar")] = b"dl019600.sar"
    for i, (offset, payload, name) in enumerate([
        (start, hkt, b"000000_idle_anm.hkt"),
        (start + len(hkt), bc, b"000001_test.bc"),
    ]):
        pos = table + i * 64
        struct.pack_into(order + "III", data, pos,
                         offset + (100000 if corrupt and i == 1 else 0),
                         len(payload), i + 1)
        data[pos+12:pos+12+len(name)] = name
        data[offset:offset+len(payload)] = payload
    return bytes(data)


def parse_fixture(data, endian):
    order = ">" if endian == "big" else "<"
    assert data[:4] == (b"SAR1" if endian == "big" else b"1RAS")
    length, version, count, table, first = struct.unpack_from(order + "5I", data, 4)
    assert length == len(data) and 0 < count <= 4096 and version > 0
    assert table >= 160 and table + count * 64 <= len(data)
    assert 160 <= first <= len(data)
    out = []
    for i in range(count):
        at = table + i * 64
        offset, size, namehash = struct.unpack_from(order + "III", data, at)
        assert size > 0 and table + count * 64 <= offset
        assert offset + size <= len(data)
        name = data[at + 12:at + 64].split(b"\0", 1)[0].decode("ascii")
        out.append((name, offset, size, namehash, data[offset:offset+8]))
    return out


def main():
    for big in (False, True):
        blob = fixture(big)
        rows = parse_fixture(blob, "big" if big else "little")
        assert rows[0][0] == "000000_idle_anm.hkt"
        assert rows[0][4] == MAGIC
        assert rows[1][0] == "000001_test.bc"
        assert rows[1][4][:4] == b"BC\0\0"
        try:
            parse_fixture(fixture(big, corrupt=True), "big" if big else "little")
        except AssertionError:
            pass
        else:
            raise AssertionError("Failed to reject out-of-bounds entry")

    required = (
        "XBXSkellSarAnimationAudit", "MaxEntries = 4096",
        "MaxArchiveBytes", "HeaderBytes = 160", "EntryBytes = 64",
        "bool bigEndian = data[0] == (byte)'S'",
        "bool littleEndian = data[0] == (byte)'1'",
        "Parse(data, bigEndian)", "version == 0x101",
        "version == 0x102", "version == 0x210",
        "name = name, nameHash = hash", "HktMagic",
        "hkaSplineCompressedAnimation", "hkaDeltaCompressedAnimation",
        "hkaInterleavedUncompressedAnimation",
        "FileMode.CreateNew", "SafeFileName", "XBX_Exports",
        "Motion Archive", "native Havok clips require decoding",
    )
    combined = AUDIT + UI
    for item in required:
        assert item in combined, "Missing archive contract: " + item

    assert 'XBXSkellSarAnimationAudit.RunSelected();' in UI
    assert 'XBXSkellSarAnimationAudit.ExtractSelected();' in UI
    assert '"Editor/XBXSkellSarAnimationAudit.cs"' in BUILDER
    assert '"Editor/XBXSkellSarAnimationAudit.cs.meta"' in BUILDER
    assert "hkc.cpk" in README and "AnimationClip" in README
    print("Synthetic 1RAS LE/BE table, bounds, HKT classification and UI/package contracts OK.")


if __name__ == "__main__":
    main()
