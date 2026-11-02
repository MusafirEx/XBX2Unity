// Wii U HKT skeleton subset. Based on ScanMountGoat/xc3_lib/src/hkt.rs (MIT).
// Read-only native Havok bone hierarchy, not a Havok animation/skin decoder.
#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace MusafirEx.XBX2Unity.Editor
{
    internal static class XBXSkellHktRig
    {
        internal sealed class Bone
        {
            internal string name;
            internal int parent;
            internal Vector3 translation;
            internal Quaternion rotation;
            internal Vector3 scale;
        }
        internal sealed class Skeleton
        {
            internal readonly List<Bone> bones = new List<Bone>();
            internal string rootName;
            internal string version;
        }
        internal static Skeleton Read(string filename)
        {
            byte[] data = File.ReadAllBytes(filename);
            if (data.Length < 720 || data.Length > 128 * 1024 * 1024)
                throw new InvalidDataException("HKT size outside supported range: " + data.Length);
            var br = new BE(data);
            byte[] magic = { 0x57, 0xE0, 0xE0, 0x57, 0x10, 0xC0, 0xC0, 0x10 };
            foreach (byte b in magic)
                if (br.U8() != b) throw new InvalidDataException("Not a Wii U Havok HKT packfile");
            br.Skip(14 * 4);
            string section1 = br.FixedString(16);
            uint[] classInfo = new uint[8];
            for (int i = 0; i < classInfo.Length; i++) classInfo[i] = br.U32();
            string section2 = br.FixedString(16);
            br.Skip(8 * 4);
            string section3 = br.FixedString(16);
            br.Skip(8 * 4);
            if (section1 != "__classnames__" || section2 != "__types__" || section3 != "__data__")
                throw new InvalidDataException("Unexpected Havok packfile sections: " +
                    section1 + ", " + section2 + ", " + section3);
            int classNamesLength = CheckedCount(classInfo[2], 8 * 1024 * 1024, "classnames data");
            br.Skip(classNamesLength);
            br.Skip(52 * 4);
            int count = CheckedCount(br.U32(), 4096, "skeleton bones");
            if (count == 0) throw new InvalidDataException("HKT has zero bones");
            br.Skip(19 * 4);
            string rootName = br.FixedString(16);
            var parents = new int[count];
            for (int i = 0; i < count; i++) parents[i] = br.I16();
            br.Align(16);
            br.Skip(8L * count);
            var names = new string[count];
            for (int i = 0; i < count; i++)
            {
                names[i] = br.CString(192);
                br.Align(16);
                if (string.IsNullOrWhiteSpace(names[i])) names[i] = "bone_" + i.ToString("D3");
            }
            var skeleton = new Skeleton { rootName = rootName, version = "Havok 2013 / Wii U" };
            for (int i = 0; i < count; i++)
            {
                // HKT Transform = vec4 translation, vec4 quaternion, vec4 scale.
                var t = new Vector3(br.F32(), br.F32(), br.F32());
                br.Skip(4);
                var q = new Quaternion(br.F32(), br.F32(), br.F32(), br.F32());
                var sc = new Vector3(br.F32(), br.F32(), br.F32());
                br.Skip(4);
                if (!Finite(t.x) || !Finite(t.y) || !Finite(t.z) ||
                    !Finite(q.x) || !Finite(q.y) || !Finite(q.z) || !Finite(q.w) ||
                    q.sqrMagnitude < 0.001f ||
                    !Finite(sc.x) || !Finite(sc.y) || !Finite(sc.z) ||
                    Mathf.Abs(sc.x) > 100 || Mathf.Abs(sc.y) > 100 || Mathf.Abs(sc.z) > 100)
                    throw new InvalidDataException("Invalid HKT bone transform " + i + " (" + names[i] + ")");
                if (parents[i] < -1 || parents[i] >= count)
                    throw new InvalidDataException("Invalid HKT parent for bone " + i + ": " + parents[i]);
                skeleton.bones.Add(new Bone {
                    name = names[i], parent = parents[i], translation = t,
                    rotation = Quaternion.Normalize(q), scale = sc
                });
            }
            return skeleton;
        }
        internal static GameObject Build(Skeleton skeleton, bool flipX, float modelScale)
        {
            if (skeleton == null || skeleton.bones.Count == 0)
                throw new InvalidDataException("No HKT skeleton bones");
            var container = new GameObject("Skell_Rig");
            var nodes = new GameObject[skeleton.bones.Count];
            for (int i = 0; i < nodes.Length; i++)
            {
                var b = skeleton.bones[i];
                nodes[i] = new GameObject(string.IsNullOrEmpty(b.name)
                    ? "bone_" + i.ToString("D3")
                    : b.name.Replace('/', '_').Replace('\\', '_'));
            }
            for (int i = 0; i < nodes.Length; i++)
            {
                var b = skeleton.bones[i];
                int parent = b.parent;
                bool valid = parent >= 0 && parent < nodes.Length && parent != i;
                if (valid)
                {
                    int cur = parent;
                    for (int step = 0; step < nodes.Length; step++)
                    {
                        if (cur == i) { valid = false; break; }
                        if (cur < 0 || cur >= nodes.Length) break;
                        cur = skeleton.bones[cur].parent;
                        if (step == nodes.Length - 1) valid = false;
                    }
                }
                nodes[i].transform.SetParent(valid ? nodes[parent].transform : container.transform, false);
                nodes[i].transform.localPosition = new Vector3(
                    flipX ? -b.translation.x : b.translation.x,
                    b.translation.y, b.translation.z) * modelScale;
                nodes[i].transform.localRotation = flipX
                    ? new Quaternion(b.rotation.x, -b.rotation.y, -b.rotation.z, b.rotation.w)
                    : b.rotation;
                nodes[i].transform.localScale = b.scale;
            }
            return container;
        }
        private static bool Finite(float value) { return !float.IsNaN(value) && !float.IsInfinity(value); }
        private static int CheckedCount(uint n, int max, string label)
        {
            if (n > max) throw new InvalidDataException("HKT " + label + " too large: " + n);
            return (int)n;
        }
        private sealed class BE
        {
            private readonly byte[] data;
            private int pos;
            internal BE(byte[] bytes) { data = bytes; }
            private void Require(long n)
            {
                if (n < 0 || (long)pos + n > data.Length)
                    throw new InvalidDataException("HKT read past end at 0x" + pos.ToString("X") +
                        " (need " + n + ", size=" + data.Length + ")");
            }
            internal byte U8() { Require(1); return data[pos++]; }
            internal void Skip(long count) { Require(count); pos += (int)count; }
            internal void Align(int n) { Skip(((long)pos + n - 1) / n * n - pos); }
            internal uint U32()
            {
                Require(4);
                return ((uint)U8() << 24) | ((uint)U8() << 16) | ((uint)U8() << 8) | U8();
            }
            internal short I16() { Require(2); return unchecked((short)(U8() << 8 | U8())); }
            internal float F32() { return BitConverter.ToSingle(BitConverter.GetBytes(U32()), 0); }
            internal string FixedString(int length)
            {
                Require(length); int start = pos; pos += length;
                int n = 0; while (n < length && data[start + n] != 0) n++;
                return Encoding.ASCII.GetString(data, start, n);
            }
            internal string CString(int max)
            {
                int start = pos;
                while (pos - start < max && pos < data.Length && data[pos] != 0) pos++;
                if (pos >= data.Length || pos - start >= max)
                    throw new InvalidDataException("Unterminated HKT bone name at 0x" + start.ToString("X"));
                string name = Encoding.UTF8.GetString(data, start, pos - start);
                pos++; return name;
            }
        }
    }
}
#endif
