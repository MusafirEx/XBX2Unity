#!/usr/bin/env python3
"""Reference GX2 D2 Thin1 address regression for compressed BC1/BC5 blocks.

Math follows ScanMountGoat/wiiu_swizzle src/addrlib.rs (MIT), 2 pipes / 4 banks.
Not a substitute for Unity import/visual verification or native C# compilation.
"""
from __future__ import annotations

from pathlib import Path


def reference(x: int, y: int, pitch: int, block_size: int, swizzle: int) -> int:
    def bit(n: int, shift: int) -> int:
        return (n >> shift) & 1

    if block_size == 8:
        pixel = (
            bit(x, 0) | bit(y, 0) << 1 | bit(x, 1) << 2 |
            bit(x, 2) << 3 | bit(y, 1) << 4 | bit(y, 2) << 5
        )
    else:
        pixel = (
            bit(y, 0) | bit(x, 0) << 1 | bit(x, 1) << 2 |
            bit(x, 2) << 3 | bit(y, 1) << 4 | bit(y, 2) << 5
        )

    # GX2 2-pipe routing is y3 XOR x3, NOT y3 XOR x4.
    pipe = bit(y, 3) ^ bit(x, 3)
    # Bank uses ty = y / numPipes = y / 2.
    ty = y // 2
    bank = (bit(ty, 4) ^ bit(x, 3)) | ((bit(ty, 3) ^ bit(x, 4)) << 1)
    bank_pipe = (pipe + 2 * bank) ^ (
        bit(swizzle, 8) + 2 * ((swizzle >> 9) & 3)
    )
    bank_pipe %= 8

    macro_bytes = 32 * 16 * block_size
    macro_offset = macro_bytes * (x // 32 + (pitch // 32) * (y // 16))
    total = pixel * block_size + macro_offset // 8

    return (
        ((total & ~255) << 3)
        | ((bank_pipe >> 1) << 9)
        | ((bank_pipe & 1) << 8)
        | (total & 255)
    )


def main() -> None:
    root = Path(__file__).resolve().parents[1]
    decoder = (root / "Editor" / "XBXSkellMtxt.cs").read_text(encoding="utf-8")
    correct = "int pipe = ((y>>3)&1) ^ ((x>>3)&1);"
    wrong = "int pipe = ((y>>3)&1) ^ ((x>>4)&1);"
    assert correct in decoder, "Native C# GX2 pipe coordinate must use x3."
    assert wrong not in decoder, "Old broken GX2 pipe computation remains."

    expected = (
        ((0, 0, 64, 8, 0), 0),
        ((8, 0, 64, 8, 0), 768),
        ((0, 8, 64, 8, 0), 256),
        ((16, 8, 64, 8, 256), 1024),
        ((16, 8, 64, 16, 0), 1280),
        ((31, 15, 64, 16, 0x600), 6384),
        ((32, 16, 64, 8, 0), 13312),
        ((37, 20, 128, 16, 0x700), 45984),
    )
    for args, golden in expected:
        assert reference(*args) == golden, (args, golden, reference(*args))

    blocks = 0
    for pitch in (32, 64, 128):
        for height in (16, 32, 64):
            for block_size in (8, 16):
                for swizzle in (0, 0x100, 0x200, 0x700):
                    offsets = {
                        reference(x, y, pitch, block_size, swizzle)
                        for y in range(height) for x in range(pitch)
                    }
                    assert len(offsets) == pitch * height, (
                        pitch, height, block_size, swizzle,
                        len(offsets), pitch * height
                    )
                    blocks += pitch * height

    print(f"GX2 BCn reference regression passed: {len(expected)} goldens, "
          f"{blocks} address entries, no collisions; C# pipe x3 verified.")


if __name__ == "__main__":
    main()
