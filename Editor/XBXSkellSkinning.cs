// Native Wii U Skell skin weight decoding. Source layout reference:
// ScanMountGoat/xc3_lib (MIT): xc3_lib/src/vertex.rs (DataType 1/2/3/4),
// xc3_model/src/vertex.rs (read_vertex_buffers_legacy), and
// xc3_model/src/skinning.rs (WeightGroups::Legacy).
// Conservative by design: unsupported/ambiguous buffers stay MeshRenderer.
#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace MusafirEx.XBX2Unity.Editor
{
    internal static class XBXSkellSkinning
    {
        internal static bool TryBuild(
            LegacyCamdoReader.Document source,
            LegacyCamdoReader.Model group,
            LegacyCamdoReader.Draw draw,
            LegacyCamdoReader.VertexBuffer vertex,
            Mesh staticMesh,
            Transform[] hktBones,
            Transform meshTransform,
            out Mesh skinnedMesh,
            out string diagnostic)
        {
            skinnedMesh = null;
            diagnostic = null;
            if (source == null || group == null || group.geometry == null ||
                staticMesh == null || hktBones == null || hktBones.Length == 0)
                return Fail("no rig/geometry", out diagnostic);
            if (source.skinBoneNames.Count == 0)
                return Fail("no MXMD skinning bone-name palette", out diagnostic);
            var weightIndex = vertex.Find(3, 4);  // WeightIndex or WeightIndex2 (Uint16x2)
            if (weightIndex == null || weightIndex.size < 4)
                return Fail("no weight-index attribute on mesh vertex buffer", out diagnostic);
            var geometry = group.geometry;
            var weightedBuffers = new List<int>();
            for (int i = 0; i < geometry.vertices.Count; i++)
            {
                var v = geometry.vertices[i];
                if (v.Find(1) != null && v.Find(2) != null)
                    weightedBuffers.Add(i);
            }
            if (weightedBuffers.Count == 0)
                return Fail("no SkinWeights2/BoneIndices2 shared buffers", out diagnostic);

            // MXMD and HKT bone order differs. NEVER assume numerical identities.
            var hktNameToIndex = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = 0; i < hktBones.Length; i++)
                if (hktBones[i] && !hktNameToIndex.ContainsKey(hktBones[i].name))
                    hktNameToIndex.Add(hktBones[i].name, i);
            var boneRemap = new int[source.skinBoneNames.Count];
            for (int i = 0; i < boneRemap.Length; i++)
            {
                if (!hktNameToIndex.TryGetValue(source.skinBoneNames[i], out int target))
                    return Fail("MXMD bone '" + source.skinBoneNames[i] +
                        "' is missing from HKT hierarchy", out diagnostic);
                boneRemap[i] = target;
            }

            List<int> orderedBuffers = ResolveWeightBuffers(geometry, weightedBuffers, draw.flags2,
                out diagnostic);
            if (orderedBuffers == null) return false;

            // Use each buffer's raw row index and preserve the source's order.
            // For flags 1/8 XC X can concatenate two shared weight buffers.
            var rows = new List<WeightRow>();
            try
            {
                foreach (int bufferIndex in orderedBuffers)
                {
                    var shared = geometry.vertices[bufferIndex];
                    var weights = shared.Find(1);
                    var indices = shared.Find(2);
                    if (weights == null || indices == null || weights.size < 12 || indices.size < 4)
                        return Fail("invalid shared skin weight stride/attribute size", out diagnostic);
                    for (int i = 0; i < shared.count; i++)
                    {
                        int start = checked(shared.dataOffset + i * shared.stride);
                        var rd = geometry.reader;
                        float x = rd.BEFloat(start + weights.offset);
                        float y = rd.BEFloat(start + weights.offset + 4);
                        float z = rd.BEFloat(start + weights.offset + 8);
                        float w = Mathf.Max(0f, 1f - (x + y + z));
                        if (!Finite(x) || !Finite(y) || !Finite(z) || x < -0.001f ||
                            y < -0.001f || z < -0.001f || x > 1.001f ||
                            y > 1.001f || z > 1.001f)
                            return Fail("invalid shared skin weight values", out diagnostic);
                        var row = new WeightRow();
                        row.weights = new Vector4(x, y, z, w);
                        for (int c = 0; c < 4; c++)
                            row.indices[c] = rd.U8(start + indices.offset + c);
                        rows.Add(row);
                    }
                }
                if (rows.Count == 0)
                    return Fail("shared skin buffers are empty", out diagnostic);

                var data = new BoneWeight[vertex.count];
                int weightedVertices = 0;
                for (int i = 0; i < vertex.count; i++)
                {
                    int address = checked(vertex.dataOffset + i * vertex.stride + weightIndex.offset);
                    int rowIndex = geometry.reader.U16(address);
                    if (rowIndex < 0 || rowIndex >= rows.Count)
                        return Fail("vertex " + i + " shared weight index " + rowIndex +
                            " outside " + rows.Count + " rows", out diagnostic);
                    WeightRow row = rows[rowIndex];
                    var weightsOut = new float[4];
                    var bonesOut = new int[4];
                    float total = 0f;
                    for (int c = 0; c < 4; c++)
                    {
                        float weight = row.weights[c];
                        if (weight < 0.00001f) continue;
                        if (row.indices[c] >= boneRemap.Length)
                            return Fail("vertex " + i + " references palette index " +
                                row.indices[c] + " outside " + boneRemap.Length, out diagnostic);
                        weightsOut[c] = weight;
                        bonesOut[c] = boneRemap[row.indices[c]];
                        total += weight;
                    }
                    if (total < 0.00001f)
                        return Fail("vertex " + i + " has no nonzero influences", out diagnostic);
                    weightedVertices++;
                    var entry = new BoneWeight();
                    entry.boneIndex0 = bonesOut[0]; entry.weight0 = weightsOut[0] / total;
                    entry.boneIndex1 = bonesOut[1]; entry.weight1 = weightsOut[1] / total;
                    entry.boneIndex2 = bonesOut[2]; entry.weight2 = weightsOut[2] / total;
                    entry.boneIndex3 = bonesOut[3]; entry.weight3 = weightsOut[3] / total;
                    data[i] = entry;
                }
                if (weightedVertices != vertex.count || staticMesh.vertexCount != vertex.count)
                    return Fail("vertex/skin weight count mismatch", out diagnostic);

                // Bind poses are derived in the importer coordinate system, AFTER
                // the same axis conversion applied to both mesh and HKT transforms.
                var poses = new Matrix4x4[hktBones.Length];
                for (int i = 0; i < poses.Length; i++)
                {
                    if (!hktBones[i])
                        return Fail("unavailable HKT transform " + i, out diagnostic);
                    poses[i] = hktBones[i].worldToLocalMatrix * meshTransform.localToWorldMatrix;
                }

                var result = UnityEngine.Object.Instantiate(staticMesh);
                result.name = staticMesh.name + "_skinned_f" + (draw.flags2 & 0xff);
                result.boneWeights = data;
                result.bindposes = poses;
                skinnedMesh = result;
                diagnostic = "skin weights " + weightedVertices + " vertices, " +
                    source.skinBoneNames.Count + " MXMD palette names, " +
                    orderedBuffers.Count + " weight buffers";
                return true;
            }
            catch (Exception e) when (e is InvalidDataException || e is OverflowException ||
                                       e is IndexOutOfRangeException)
            {
                return Fail("invalid weight data: " + e.Message, out diagnostic);
            }
        }

        private sealed class WeightRow
        {
            internal Vector4 weights;
            internal readonly int[] indices = new int[4];
        }

        private static List<int> ResolveWeightBuffers(
            LegacyCamdoReader.Geometry geometry,
            List<int> all,
            uint flags,
            out string diagnostic)
        {
            diagnostic = null;
            if (all.Count == 0)
                return NoBuffers(out diagnostic);
            // XCX legacy VertexData keeps six indices into the ORIGINAL vertex
            // buffer array. xc3_model/src/vertex.rs removes all weight buffers
            // starting at the first weight buffer; its weight_buffer_indices are
            // mapped with checked_sub(weight_buffer_start). An absent/earlier
            // selector becomes None, which xc3_model/src/skinning.rs resolves
            // with .unwrap_or_default() => ordinal zero.
            //
            // v0.7.1 used "all.Contains(raw)" instead and rejected 53 of the 54
            // draw calls in dl019100 with "slot 4=0". This is incorrect.
            int firstWeightBuffer = all[0];
            int selectorFlags = (int)(flags & 0xff);
            int[] slots;
            if (selectorFlags == 1)
            {
                // Preserve both concatenated parts even if they refer to the
                // same buffer: xc3_model concatenates, it does not deduplicate.
                int slot4 = ToWeightOrdinal(geometry.weightBufferIndices[4],
                    firstWeightBuffer, all.Count);
                if (slot4 < 0)
                {
                    diagnostic = "MXMD skin selector 4 points outside weight buffers";
                    return null;
                }
                slots = slot4 > 0 ? new[] { 4, 0 } : new[] { 0, 4 };
            }
            else if (selectorFlags == 8) slots = new[] { 3, 4 };
            else if (selectorFlags == 2 || selectorFlags == 64) slots = new[] { 1 };
            else if (selectorFlags == 0x21) slots = new[] { 4 };
            else slots = new[] { -1 }; // xc3_model first() for other flags.

            var chosen = new List<int>(slots.Length);
            foreach (int slot in slots)
            {
                if (slot == -1)
                {
                    chosen.Add(all[0]);
                    continue;
                }
                int raw = geometry.weightBufferIndices[slot];
                int ordinal = ToWeightOrdinal(raw, firstWeightBuffer, all.Count);
                if (ordinal < 0)
                {
                    diagnostic = "weight header slot " + slot + "=" + raw +
                        " resolves outside the " + all.Count + " skin buffers " +
                        "(original first buffer=" + firstWeightBuffer + ")";
                    return null;
                }
                chosen.Add(all[ordinal]);
            }
            return chosen;
        }

        private static int ToWeightOrdinal(int raw, int first, int count)
        {
            // checked_sub(first) -> None -> unwrap_or_default(0) in xc3_model.
            int ordinal = raw < first ? 0 : raw - first;
            return ordinal < count ? ordinal : -1;
        }

        private static List<int> NoBuffers(out string reason)
        {
            reason = "no shared weight buffers";
            return null;
        }

        private static bool Finite(float f) { return !float.IsNaN(f) && !float.IsInfinity(f); }
        private static bool Fail(string message, out string reason)
        { reason = message; return false; }
    }
}
#endif
