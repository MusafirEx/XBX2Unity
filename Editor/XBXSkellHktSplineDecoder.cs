// Xenoblade X (Wii U) Havok spline motion: 32-bit hk_2013 single-block decoder.
// Independent implementation using verified native HKT input and standard
// de Boor spline evaluation. No third-party GPL source files are copied.
// Raw HKT header uses big-endian; the packed m_data block uses little-endian.
#if UNITY_EDITOR
using System;
using System.IO;
using UnityEngine;

namespace MusafirEx.XBX2Unity.Editor
{
    internal static class XBXSkellHktSplineDecoder
    {
        internal sealed class Poses
        {
            internal Vector3[][] positions;
            internal Quaternion[][] rotations;
            internal Vector3[][] scales;
            internal int trackCount;
            internal int frameCount;
            internal float duration;
            internal int dynamicTranslations;
            internal int dynamicRotations;
            internal int dynamicScales;
        }

        private sealed class Axis
        {
            internal int mode; // 0 = identity, 1 = static, 2 = spline
            internal float constant;
            internal byte[] knots;
            internal float[] values;
            internal int degree;
        }

        private sealed class Rot
        {
            internal int mode;
            internal Quaternion constant = Quaternion.identity;
            internal byte[] knots;
            internal Quaternion[] values;
            internal int degree;
        }

        private sealed class Track
        {
            internal Axis[] position;
            internal Axis[] scale;
            internal Rot rotation;
        }

        private sealed class Reader
        {
            private readonly byte[] bytes;
            internal int Position { get; private set; }
            internal Reader(byte[] data) { bytes = data; }
            internal int Size { get { return bytes.Length; } }
            internal byte U8()
            {
                if (Position >= bytes.Length)
                    throw new InvalidDataException("Spline data is truncated at " + Position);
                return bytes[Position++];
            }
            internal ushort U16()
            {
                int a = U8(), b = U8();
                return (ushort)(a | (b << 8));
            }
            internal float F32()
            {
                uint a = U8(), b = U8(), c = U8(), d = U8();
                uint bits = a | (b << 8) | (c << 16) | (d << 24);
                float val = BitConverter.ToSingle(BitConverter.GetBytes(bits), 0);
                if (float.IsNaN(val) || float.IsInfinity(val) || Mathf.Abs(val) > 1000000f)
                    throw new InvalidDataException("Spline float not finite / out of range");
                return val;
            }
            internal byte[] Bytes(int count)
            {
                if (count < 0 || (long)Position + count > bytes.Length)
                    throw new InvalidDataException("Spline array exceeds compressed input");
                byte[] data = new byte[count];
                Buffer.BlockCopy(bytes, Position, data, 0, count);
                Position += count;
                return data;
            }
            internal void Align(int boundary)
            {
                int next = (Position + boundary - 1) / boundary * boundary;
                if (next > bytes.Length)
                    throw new InvalidDataException("Spline alignment exceeds input");
                // Accept only 0x00/0xFF padding when advancing to the boundary.
                for (; Position < next; Position++)
                    if (bytes[Position] != 0 && bytes[Position] != 0xff)
                        throw new InvalidDataException(
                            "Unexpected spline alignment padding at 0x" + Position.ToString("X"));
            }
        }

        internal static Poses Decode(XBXSkellHktMotionAudit.MotionProbe source)
        {
            if (source == null || source.packedSplineData == null)
                throw new InvalidDataException("Missing verified HKT spline source");
            if (source.blockCount != 1 || source.floatTrackCount != 0)
                throw new NotSupportedException(
                    "Only one Havok spline block and zero float tracks are verified so far.");
            if (source.trackCount < 1 || source.trackCount > 1024 ||
                source.frameCount < 2 || source.frameCount > 4096 ||
                (long)source.trackCount * source.frameCount > 250000)
                throw new InvalidDataException("HKT clip exceeds bounded sample budget");
            if (source.maskAndQuantizationBytes != source.trackCount * 4)
                throw new InvalidDataException("Transform masks are not a packed 4-byte table");

            byte[] raw = source.packedSplineData;
            var reader = new Reader(raw);
            var masks = new byte[source.trackCount][];
            for (int i = 0; i < masks.Length; i++)
                masks[i] = reader.Bytes(4);
            reader.Align(4);

            var parsed = new Track[source.trackCount];
            int translated = 0, rotated = 0, scaled = 0;
            for (int t = 0; t < parsed.Length; t++)
            {
                byte q = masks[t][0];
                int pquant = q & 3;
                int rquant = (q >> 2) & 15;
                int squant = (q >> 6) & 3;
                if (pquant > 1 || rquant != 1 || squant > 1)
                    throw new NotSupportedException("HKT track " + t +
                        " has unsupported quantization mask 0x" + q.ToString("X2") +
                        "; this decoder currently supports THREECOMP40 rotation.");
                var track = new Track();
                track.position = ReadVector(reader, masks[t][1], pquant == 1, 0f);
                reader.Align(4);
                track.rotation = ReadRotation(reader, masks[t][2]);
                reader.Align(4);
                track.scale = ReadVector(reader, masks[t][3], squant == 1, 1f);
                reader.Align(4);
                parsed[t] = track;
                for (int axis = 0; axis < 3; axis++)
                {
                    if (track.position[axis].mode == 2) translated++;
                    if (track.scale[axis].mode == 2) scaled++;
                }
                if (track.rotation.mode == 2) rotated++;
            }
            reader.Align(16);
            if (reader.Position != reader.Size)
                throw new InvalidDataException("Spline stream has unused bytes: stopped at 0x" +
                    reader.Position.ToString("X") + " of 0x" + reader.Size.ToString("X"));

            var poses = new Poses
            {
                trackCount = source.trackCount, frameCount = source.frameCount,
                duration = source.durationSeconds,
                positions = new Vector3[source.trackCount][],
                rotations = new Quaternion[source.trackCount][],
                scales = new Vector3[source.trackCount][],
                dynamicTranslations = translated, dynamicRotations = rotated,
                dynamicScales = scaled
            };
            for (int t = 0; t < source.trackCount; t++)
            {
                Track track = parsed[t];
                poses.positions[t] = new Vector3[source.frameCount];
                poses.rotations[t] = new Quaternion[source.frameCount];
                poses.scales[t] = new Vector3[source.frameCount];
                Quaternion previous = Quaternion.identity;
                for (int f = 0; f < source.frameCount; f++)
                {
                    Vector3 p = new Vector3(
                        Evaluate(track.position[0], f),
                        Evaluate(track.position[1], f),
                        Evaluate(track.position[2], f));
                    Vector3 scale = new Vector3(
                        Evaluate(track.scale[0], f),
                        Evaluate(track.scale[1], f),
                        Evaluate(track.scale[2], f));
                    Quaternion q = Evaluate(track.rotation, f);
                    float length2 = q.x*q.x + q.y*q.y + q.z*q.z + q.w*q.w;
                    if (float.IsNaN(length2) || length2 < 0.01f ||
                        length2 > 100f || p.sqrMagnitude > 1e12f ||
                        scale.sqrMagnitude > 1e10f)
                        throw new InvalidDataException("Decoded spline pose is implausible: track " +
                            t + " frame " + f);
                    q = Quaternion.Normalize(q);
                    if (f > 0 && Quaternion.Dot(previous, q) < 0f)
                        q = new Quaternion(-q.x, -q.y, -q.z, -q.w);
                    poses.positions[t][f] = p;
                    poses.rotations[t][f] = q;
                    poses.scales[t][f] = scale;
                    previous = q;
                }
            }
            return poses;
        }

        private static Axis[] ReadVector(Reader r, byte mask, bool sixteenBit, float identity)
        {
            var result = new Axis[3];
            for (int i = 0; i < 3; i++)
                result[i] = new Axis { mode = 0, constant = identity };
            int dynamicBits = (mask >> 4) & 7;
            int staticBits = mask & 7;
            if ((dynamicBits & staticBits) != 0 || (mask & 0x88) != 0)
                throw new InvalidDataException("Invalid vector mask 0x" + mask.ToString("X2"));
            byte[] knots = null;
            int degree = 0, count = 0;
            if (dynamicBits != 0)
                ReadKnotHeader(r, out count, out degree, out knots);
            r.Align(4);
            float[] min = new float[3], max = new float[3];
            for (int i = 0; i < 3; i++)
            {
                if ((dynamicBits & (1 << i)) != 0)
                {
                    min[i] = r.F32();
                    max[i] = r.F32();
                    if (min[i] > max[i])
                        throw new InvalidDataException("Decreasing spline quantization bounds");
                    result[i].mode = 2;
                    result[i].degree = degree;
                    result[i].knots = knots;
                    result[i].values = new float[count];
                }
                else if ((staticBits & (1 << i)) != 0)
                {
                    result[i].mode = 1;
                    result[i].constant = r.F32();
                }
            }
            for (int j = 0; j < count; j++)
                for (int i = 0; i < 3; i++)
                    if (result[i].mode == 2)
                    {
                        int encoded = sixteenBit ? r.U16() : r.U8();
                        float fraction = encoded / (sixteenBit ? 65535f : 255f);
                        result[i].values[j] =
                            min[i] + (max[i] - min[i]) * fraction;
                    }
            return result;
        }

        private static Rot ReadRotation(Reader r, byte mask)
        {
            var result = new Rot();
            if ((mask & 0xF0) != 0)
            {
                int controls, degree;
                byte[] knots;
                ReadKnotHeader(r, out controls, out degree, out knots);
                result.mode = 2;
                result.degree = degree;
                result.knots = knots;
                result.values = new Quaternion[controls];
                for (int i = 0; i < controls; i++)
                    result.values[i] = ReadQuat40(r);
            }
            else if ((mask & 0x0F) != 0)
            {
                result.mode = 1;
                result.constant = ReadQuat40(r);
            }
            return result;
        }

        private static void ReadKnotHeader(Reader r, out int controls,
            out int degree, out byte[] knots)
        {
            controls = r.U16() + 1;
            degree = r.U8();
            if (controls < 2 || controls > 512 || degree > 4 || degree >= controls)
                throw new InvalidDataException("Unsupported Havok B-spline controls/degree");
            knots = r.Bytes(checked(controls + degree + 1));
            for (int i = 1; i < knots.Length; i++)
                if (knots[i] < knots[i - 1])
                    throw new InvalidDataException("Havok spline knots not monotonic");
        }

        private static Quaternion ReadQuat40(Reader r)
        {
            ulong bits = 0;
            for (int i = 0; i < 5; i++)
                bits |= (ulong)r.U8() << (i * 8);
            const float factor = 1.4142135623730951f / 4095f;
            float a = (((int)(bits & 0xFFF)) - 2047) * factor;
            float b = (((int)((bits >> 12) & 0xFFF)) - 2047) * factor;
            float c = (((int)((bits >> 24) & 0xFFF)) - 2047) * factor;
            float m = Mathf.Sqrt(Mathf.Max(0f, 1f - a*a - b*b - c*c));
            if (((bits >> 38) & 1) != 0) m = -m;
            int missingIndex = (int)((bits >> 36) & 3);
            var components = new float[4];
            var three = new[] { a, b, c };
            for (int k = 0, input = 0; k < 4; k++)
                components[k] = k == missingIndex ? m : three[input++];
            return new Quaternion(components[0], components[1],
                components[2], components[3]);
        }

        private static int KnotSpan(byte[] knots, int count, int degree, float frame)
        {
            if (frame >= knots[count]) return count - 1;
            if (frame <= knots[degree]) return degree;
            int lo = degree, hi = count;
            while (hi - lo > 1)
            {
                int mid = (lo + hi) / 2;
                if (frame < knots[mid]) hi = mid;
                else lo = mid;
            }
            return lo;
        }

        private static float Evaluate(Axis axis, float frame)
        {
            if (axis.mode != 2) return axis.constant;
            int count = axis.values.Length;
            int degree = axis.degree;
            int span = KnotSpan(axis.knots, count, degree, frame);
            var values = new float[degree + 1];
            for (int i = 0; i <= degree; i++)
                values[i] = axis.values[span - degree + i];
            for (int step = 1; step <= degree; step++)
                for (int i = degree; i >= step; i--)
                {
                    int knot = span - degree + i;
                    float width = axis.knots[span + 1 + i - step] -
                        axis.knots[knot];
                    float fraction = width <= 0 ? 0 : (frame - axis.knots[knot]) / width;
                    values[i] = (1 - fraction) * values[i - 1] + fraction * values[i];
                }
            return values[degree];
        }

        private static Quaternion Evaluate(Rot track, float frame)
        {
            if (track.mode != 2) return track.constant;
            int count = track.values.Length;
            int degree = track.degree;
            int span = KnotSpan(track.knots, count, degree, frame);
            var values = new Quaternion[degree + 1];
            for (int i = 0; i <= degree; i++)
                values[i] = track.values[span - degree + i];
            for (int step = 1; step <= degree; step++)
                for (int i = degree; i >= step; i--)
                {
                    int knot = span - degree + i;
                    float width = track.knots[span + 1 + i - step] -
                        track.knots[knot];
                    float fraction = width <= 0 ? 0 : (frame - track.knots[knot]) / width;
                    Quaternion a = values[i - 1], b = values[i];
                    values[i] = new Quaternion(
                        a.x + (b.x - a.x) * fraction,
                        a.y + (b.y - a.y) * fraction,
                        a.z + (b.z - a.z) * fraction,
                        a.w + (b.w - a.w) * fraction);
                }
            return values[degree];
        }
    }
}
#endif
