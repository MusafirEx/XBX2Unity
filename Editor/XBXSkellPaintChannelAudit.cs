// Read-only BC1 channel profile, measured on GX2-deswizzled source
// 4x4 blocks before Texture2D.Apply(false,true) discards CPU pixels.
// No assumptions about Skell paint semantics: this is sampled DATA only.
#if UNITY_EDITOR
using System;
using System.Globalization;

namespace MusafirEx.XBX2Unity.Editor
{
    internal static class XBXSkellPaintChannelAudit
    {
        internal static string ProfileBc1(byte[] blocks, int width, int height)
        {
            if (blocks == null || width <= 0 || height <= 0)
                return "No BC1 sample data";
            int blocksX = (width + 3) / 4;
            int blocksY = (height + 3) / 4;
            if ((long)blocksX * blocksY * 8 > blocks.Length)
                return "Invalid BC1 block data";
            // Max about 16384 samples per texture, independent of texture size.
            int stride = Math.Max(1, (int)Math.Ceiling(Math.Sqrt(
                (double)width * height / 16384.0)));
            long[] sum = new long[4], nearZero = new long[4], nearOne = new long[4];
            int[] min = { 255,255,255,255 }, max = new int[4];
            int count = 0;
            int chromaCandidates = 0;
            for (int y = 0; y < height; y += stride)
                for (int x = 0; x < width; x += stride)
                {
                    int block = ((y / 4) * blocksX + (x / 4)) * 8;
                    int c0 = blocks[block] | (blocks[block + 1] << 8);
                    int c1 = blocks[block + 2] | (blocks[block + 3] << 8);
                    uint colorIndexBits = (uint)(
                        blocks[block + 4] |
                        (blocks[block + 5] << 8) |
                        (blocks[block + 6] << 16) |
                        (blocks[block + 7] << 24));
                    int pixel = ((y & 3) << 2) | (x & 3);
                    int index = (int)((colorIndexBits >> (pixel * 2)) & 3u);
                    int[] a = Decode565(c0), b = Decode565(c1);
                    int[] rgba = { 0,0,0,255 };
                    for (int ch = 0; ch < 3; ch++)
                    {
                        if (index == 0) rgba[ch] = a[ch];
                        else if (index == 1) rgba[ch] = b[ch];
                        else if (c0 > c1)
                            rgba[ch] = index == 2 ? (2*a[ch]+b[ch])/3 :
                                (a[ch]+2*b[ch])/3;
                        else if (index == 2)
                            rgba[ch] = (a[ch]+b[ch])/2;
                        else rgba[ch] = 0;
                    }
                    if (c0 <= c1 && index == 3) rgba[3] = 0;
                    for (int ch = 0; ch < 4; ch++)
                    {
                        int value = rgba[ch];
                        min[ch] = Math.Min(min[ch], value);
                        max[ch] = Math.Max(max[ch], value);
                        sum[ch] += value;
                        if (value <= 16) nearZero[ch]++;
                        if (value >= 239) nearOne[ch]++;
                    }
                    // Count pixels dominated by one color channel; this
                    // is only a CLUE of channel masking, not proof.
                    int high = Math.Max(rgba[0], Math.Max(rgba[1],rgba[2]));
                    int low = Math.Min(rgba[0], Math.Min(rgba[1],rgba[2]));
                    if (high >= 160 && low <= 48) chromaCandidates++;
                    count++;
                }
            var inv = CultureInfo.InvariantCulture;
            string[] names = { "R","G","B","A" };
            var result = new System.Text.StringBuilder();
            result.Append("BC1 sample n=").Append(count).Append(", stride=").Append(stride);
            for (int i = 0; i < 4; i++)
            {
                result.Append(" | ").Append(names[i]).Append(" min/avg/max=")
                    .Append(min[i]).Append('/')
                    .Append((sum[i]/(double)Math.Max(1,count)).ToString("F1",inv))
                    .Append('/').Append(max[i]).Append(" near0=")
                    .Append((100.0*nearZero[i]/Math.Max(1,count)).ToString("F1",inv))
                    .Append("% near255=")
                    .Append((100.0*nearOne[i]/Math.Max(1,count)).ToString("F1",inv))
                    .Append('%');
            }
            result.Append(" | chromatic=").Append(
                (100.0*chromaCandidates/Math.Max(1,count)).ToString("F1",inv))
                .Append("% | BC1 alpha is binary (0/255), NOT four gradient mask channels");
            return result.ToString();
        }

        private static int[] Decode565(int value)
        {
            int r = (value >> 11) & 31;
            int g = (value >> 5) & 63;
            int b = value & 31;
            return new[] {
                (r << 3) | (r >> 2),
                (g << 2) | (g >> 4),
                (b << 3) | (b >> 2)
            };
        }
    }
}
#endif
