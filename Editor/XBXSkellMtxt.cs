// Native Wii U GX2 MTXT base-mip preview: BC1/BC5, tile 2/4.
// Source layouts: ScanMountGoat/xc3_lib (MIT): mtxt.rs, mxmd/legacy.rs,
// msrd.rs; address calculation: ScanMountGoat/wiiu_swizzle/addrlib.rs.
// The original file is read-only, unsupported surfaces remain unbound.
#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace MusafirEx.XBX2Unity.Editor
{
    internal static class XBXSkellMtxt
    {
        internal sealed class Record
        {
            internal int id, width, height, format, usage;
            internal string source, sourceName;
            internal Texture2D texture;
            internal bool IsNormalData
            {
                get
                {
                    string n = (sourceName ?? "").ToUpperInvariant();
                    return format == 53 || usage == 18 || usage == 530 ||
                        n.Contains("_NRM") || n.Contains("_NM") || n.Contains("NORMAL");
                }
            }
            internal int ColorScore
            {
                get
                {
                    if (IsNormalData || format != 49 || usage == 16 ||
                        usage == 528 || usage == 65569) return -1;
                    string n = (sourceName ?? "").ToUpperInvariant();
                    // Xenoblade X can label SPM/RFM/RF/GLW/GLO textures
                    // with usage 32 (which also appears on color maps).
                    // Match source suffixes BEFORE the broad usage hint.
                    if (n.EndsWith("_SPM", StringComparison.Ordinal) ||
                        n.EndsWith("_GLO", StringComparison.Ordinal) ||
                        n.EndsWith("_GLW", StringComparison.Ordinal) ||
                        n.EndsWith("_RFM", StringComparison.Ordinal) ||
                        n.EndsWith("_REF", StringComparison.Ordinal) ||
                        n.EndsWith("_RF", StringComparison.Ordinal) ||
                        n.EndsWith("_OCL", StringComparison.Ordinal) ||
                        n.EndsWith("_DEP", StringComparison.Ordinal))
                        return -1;
                    // Explicit color suffix is a stronger hint than a generic
                    // "usage 32" that some non-color textures share.
                    if (n.EndsWith("_RGB", StringComparison.Ordinal) ||
                        n.EndsWith("_COL", StringComparison.Ordinal) ||
                        n.EndsWith("_DCL", StringComparison.Ordinal) ||
                        n.EndsWith("_AVA", StringComparison.Ordinal))
                        return 160;
                    if (usage == 80) return 150;
                    if (usage == 96 || usage == 48) return 140;
                    if (usage == 32 || usage == 34 || usage == 544) return 90;
                    return 20; // BC1 without a semantic usage hint
                }
            }
        }
        private static uint U32(byte[] data, int p)
        {
            Check(data, p, 4, "header");
            return (uint)(data[p] << 24 | data[p+1] << 16 | data[p+2] << 8 | data[p+3]);
        }
        private static int I32(byte[] data, int p) {
            uint n = U32(data, p);
            if (n > int.MaxValue) throw new InvalidDataException("MTXT value overflows Int32");
            return (int)n;
        }
        private static int U16(byte[] data, int p)
        {
            Check(data, p, 2, "index");
            return data[p] << 8 | data[p+1];
        }
        private static void Check(byte[] data, long at, long size, string field)
        {
            if (at < 0 || size < 0 || at > data.Length || size > data.Length - at)
                throw new InvalidDataException("MTXT " + field + " out of bounds " + at + "+" + size);
        }
        private static int Ptr(byte[] data, int root, int relativeField)
        {
            uint n = U32(data, root + relativeField);
            if (n == 0) return -1;
            long p = (long)root + n;
            Check(data, p, 1, "pointer");
            return (int)p;
        }

        // String offsets in legacy PackedTexture and PackedExternalTexture are
        // relative to the owning PackedTextures pool, not to the descriptor.
        private static string ReadName(byte[] data, int pool, uint relative)
        {
            if (relative == 0) return "";
            long at = (long)pool + relative;
            if (at < 0 || at >= data.Length) return "";
            int start = (int)at, end = start;
            while (end < data.Length && end < start + 96 && data[end] != 0) end++;
            if (end == data.Length || end == start + 96) return "";
            for (int i = start; i < end; i++)
                if (data[i] < 32 || data[i] > 126) return "";
            return System.Text.Encoding.ASCII.GetString(data, start, end - start);
        }

        internal static Dictionary<int, Record> Decode(byte[] camdo, string casmtPath,
            int limit, out string message)
        {
            var results = new Dictionary<int, Record>();
            var warnings = new List<string>();
            try
            {
                int packed = Ptr(camdo, 0, 0x1c);
                if (packed >= 0)
                {
                    int count = I32(camdo, packed);
                    int arr = Ptr(camdo, packed, 4);
                    if (count > 4096) throw new InvalidDataException("Packed count too large");
                    if (count > 0) Check(camdo, arr, count * 16L, "packed entries");
                    for (int i = 0; i < count && i < limit; i++)
                    {
                        int p = arr + i * 16;
                        uint n = U32(camdo, p + 8);
                        int size = I32(camdo, p + 4);
                        long start = (long)packed + n;
                        if (n == 0 || size < 112 || start < 0 ||
                            start + size > camdo.Length) continue;
                        Record record = DecodeOne(camdo, (int)start, size, "packed", i, warnings);
                        if (record != null)
                        {
                            record.usage = unchecked((int)U32(camdo, p));
                            record.sourceName = ReadName(camdo, packed, U32(camdo, p + 12));
                            results[i] = record;
                        }
                    }
                }
            }
            catch (Exception e) when (e is InvalidDataException || e is OverflowException)
            { warnings.Add("Packed table: " + e.Message); }

            if (File.Exists(casmtPath))
            {
                try
                {
                    long length = new FileInfo(casmtPath).Length;
                    if (length > 128L * 1024 * 1024)
                        throw new InvalidDataException("CASMT exceeds 128 MiB texture preview limit");
                    byte[] casmt = File.ReadAllBytes(casmtPath);
                    int stream = Ptr(camdo, 0, 0x24);
                    if (stream >= 0)
                    {
                        // StreamingDataLegacyInner: +8 low textures, +12 high,
                        // +16 low texture material IDs, +20 high->low indices.
                        int lowPool = Ptr(camdo, stream, 8);
                        int highPool = Ptr(camdo, stream, 12);
                        int lowList = Ptr(camdo, stream, 16);
                        int highList = Ptr(camdo, stream, 20);
                        int lowData = I32(camdo, stream + 24);
                        int highData = I32(camdo, stream + 32);
                        var lowIds = new List<int>();
                        if (lowPool >= 0)
                            Pool(camdo, casmt, lowPool, lowList, lowData,
                                null, lowIds, results, limit, warnings);
                        if (highPool >= 0)
                            Pool(camdo, casmt, highPool, highList, highData,
                                lowIds, null, results, limit, warnings);
                    }
                }
                catch (Exception e) when (e is InvalidDataException || e is OverflowException)
                { warnings.Add("Streamed table: " + e.Message); }
            }
            message = "Decoded MTXT base textures=" + results.Count +
                (warnings.Count > 0 ? "; " + string.Join("; ", warnings.ToArray()) : "");
            return results;
        }
        private static void Pool(byte[] header, byte[] casmt, int pool, int indices,
            int dataStart, List<int> highToLow, List<int> lowIds,
            Dictionary<int, Record> textures, int limit, List<string> warnings)
        {
            int count = I32(header, pool);
            int table = Ptr(header, pool, 4);
            if (count > 4096) throw new InvalidDataException("Pool too large");
            if (count > 0) Check(header, table, (long)count * 16, "pool descriptors");
            if (indices >= 0) Check(header, indices, (long)count * 2, "pool index list");
            for (int i = 0; i < count; i++)
            {
                int texId = indices < 0 ? i : U16(header, indices + i * 2);
                if (highToLow != null)
                {
                    if (texId >= highToLow.Count) continue;
                    texId = highToLow[texId];
                }
                lowIds?.Add(texId);
                if (textures.Count >= limit && !textures.ContainsKey(texId)) continue;
                int entry = table + i * 16;
                int length = I32(header, entry + 4);
                int relative = I32(header, entry + 8);
                long start = (long)dataStart + relative;
                if (length < 112 || start < 0 || start + length > casmt.Length) continue;
                Record record = DecodeOne(casmt, (int)start, length,
                    highToLow != null ? "high" : "low", texId, warnings);
                if (record == null) continue;
                record.usage = unchecked((int)U32(header, entry));
                record.sourceName = ReadName(header, pool, U32(header, entry + 12));
                if (textures.TryGetValue(texId, out Record old) && old.texture != null)
                    UnityEngine.Object.DestroyImmediate(old.texture);
                textures[texId] = record;
            }
        }

        private static Record DecodeOne(byte[] data, int at, int length, string source,
            int id, List<string> warnings)
        {
            int footer = at + length - 112;
            if (footer < 0 || footer + 112 > data.Length ||
                data[footer+108] != (byte)'M' || data[footer+109] != (byte)'T' ||
                data[footer+110] != (byte)'X' || data[footer+111] != (byte)'T') return null;
            int width = I32(data, footer+8), height = I32(data, footer+12);
            int dimension = I32(data, footer+4), depth = I32(data, footer+16);
            int format = I32(data, footer+24);
            int mipSize = I32(data, footer+28);
            int tileMode = I32(data, footer+36);
            int pitch = I32(data, footer+48);
            int swizzle = I32(data, footer);
            int version = I32(data, footer+104);
            if (width < 4 || height < 4 || width > 4096 || height > 4096 ||
                dimension != 1 || depth != 1 || (format != 49 && format != 53) ||
                (tileMode != 2 && tileMode != 4) ||
                (version != 10001 && version != 10002) ||
                pitch < 1 || pitch > 8192 || mipSize < 1 || mipSize > footer-at) return null;
            try
            {
                int bytesPerBlock = format == 49 ? 8 : 16;
                int blocksX = (width+3)/4, blocksY=(height+3)/4;
                int expected = checked(blocksX * blocksY * bytesPerBlock);
                if (expected > 32*1024*1024)
                    throw new InvalidDataException("Texture exceeds 32 MiB decode cap");
                var raw = new byte[expected];
                for (int y = 0; y < blocksY; y++)
                    for (int x = 0; x < blocksX; x++)
                    {
                        int src = Address(x, y, pitch, tileMode, bytesPerBlock, swizzle);
                        if (src < 0 || src > mipSize - bytesPerBlock)
                            throw new InvalidDataException("GX2 swizzle address outside mip data");
                        Buffer.BlockCopy(data, at + src, raw,
                            (y*blocksX+x)*bytesPerBlock, bytesPerBlock);
                    }
                var tex = new Texture2D(width, height,
                    format == 49 ? TextureFormat.DXT1 : TextureFormat.BC5,
                    false, format == 53);
                tex.name = "MTXT_" + source + "_" + id.ToString("D3") +
                    (format == 53 ? "_BC5" : "_BC1");
                tex.LoadRawTextureData(raw);
                tex.Apply(false, true);
                return new Record { id=id, width=width, height=height, format=format,
                    source=source, texture=tex };
            }
            catch (Exception e)
            {
                if (warnings.Count < 12) warnings.Add(source+"["+id+"]: "+e.Message);
                return null;
            }
        }

        // GX2 addrlib: 2 pipes, 4 banks, 256-byte interleave.
        // Compressed 4x4 BCn blocks are the addressable elements, bpp=64/128.
        private static int Address(int x, int y, int pitch, int tile,
            int blockSize, int swizzle)
        {
            int x0=x&1, x1=(x>>1)&1, x2=(x>>2)&1;
            int y0=y&1, y1=(y>>1)&1, y2=(y>>2)&1;
            int pixel = blockSize==8 ?
                x0 | y0<<1 | x1<<2 | x2<<3 | y1<<4 | y2<<5 :
                y0 | x0<<1 | x1<<2 | x2<<3 | y1<<4 | y2<<5;
            int elem = pixel * blockSize;
            if (tile == 2)
                return 64*blockSize * (x/8 + (y/8)*(pitch/8)) + elem;
            // GX2 addrlib: 2-pipe mode uses pipe_bit0 = y3 XOR x3.
            // v0.8.1 incorrectly read x4 here and scrambled source
            // BC1/BC5 blocks even with otherwise valid UV coordinates.
            int pipe = ((y>>3)&1) ^ ((x>>3)&1);
            // addrlib uses ty=y/numPipes (2), not y itself.
            // bank0 = ty4 ^ x3 = y5 ^ x3;
            // bank1 = ty3 ^ x4 = y4 ^ x4.
            int bank = (((y>>5)&1) ^ ((x>>3)&1)) |
                ((((y>>4)&1) ^ ((x>>4)&1)) << 1);
            int bankPipe = pipe + 2*bank;
            bankPipe ^= ((swizzle>>8)&1) + 2*((swizzle>>9)&3);
            bankPipe &= 7;
            pipe = bankPipe & 1; bank = bankPipe >> 1;
            int macro = checked((32 * 16 * blockSize) *
                (x/32 + (pitch/32)*(y/16)));
            int total = elem + (macro >> 3);
            return ((total & ~255)<<3) | (bank<<9) | (pipe<<8) | (total&255);
        }
    }
}
#endif
