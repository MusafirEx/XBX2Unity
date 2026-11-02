// Native Wii U CAMDO (MXMD v10040) importer for Unity 2022.3+.
// No glTF/FBX conversion, external CLI, native DLL, or game data bundled.
// Format reference: ScanMountGoat/xc3_lib (MIT), xc3_lib/src/mxmd/legacy.rs,
// xc3_lib/src/vertex.rs and xc3_model/src/model/import.rs.
// This is a bounded static-mesh subset: rigging/animations/shader material mapping
// and procedural model instances are NOT implemented.
#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.AssetImporters;
using UnityEngine;
using UnityEngine.Rendering;

namespace MusafirEx.XBX2Unity.Editor
{
    [ScriptedImporter(1, "camdo")]
    public sealed class XBXCamdoNativeImporter : ScriptedImporter
    {
        [Header("Model")]
        [Min(0.000001f)] public float modelScale = 1f;
        public bool flipX = true;
        public bool reverseFaces = false;
        public bool flipUVv = false;
        public bool importVertexColors = true;
        [Header("Material")]
        public bool createPlaceholderMaterials = true;

        public override void OnImportAsset(AssetImportContext ctx)
        {
            GameObject root = new GameObject(Path.GetFileNameWithoutExtension(ctx.assetPath));
            int imported = 0;
            try
            {
                string companion = Path.ChangeExtension(ctx.assetPath, ".casmt");
                if (File.Exists(companion)) ctx.DependsOnSourceAsset(companion);

                // The original Wii U MXMD .camdo stores the vertex tables in CAMDO.
                // The adjacent .casmt contains streaming textures, which this
                // first native geometry importer deliberately does not parse.
                var bytes = File.ReadAllBytes(ctx.assetPath);
                var reader = new LegacyCamdoReader(bytes);
                var document = reader.Parse();

                Dictionary<int, Material> materials = new Dictionary<int, Material>();
                var geometryCache = new Dictionary<long, Mesh>();
                int meshLimit = 8000;

                for (int modelIndex = 0; modelIndex < document.models.Count; modelIndex++)
                {
                    var sourceModel = document.models[modelIndex];
                    GameObject parent = new GameObject("model_" + modelIndex.ToString("D3"));
                    parent.transform.SetParent(root.transform, false);
                    for (int drawIndex = 0; drawIndex < sourceModel.meshes.Count; drawIndex++)
                    {
                        if (imported >= meshLimit)
                        {
                            ctx.LogImportWarning("CAMDO mesh limit reached (" + meshLimit + "). Further draw calls were not imported.");
                            break;
                        }

                        var draw = sourceModel.meshes[drawIndex];
                        if (draw.vertexBufferIndex >= document.vertices.Count ||
                            draw.indexBufferIndex >= document.indices.Count)
                        {
                            ctx.LogImportWarning("Invalid buffer index in model " + modelIndex + ", mesh " + drawIndex);
                            continue;
                        }

                        // Reuse geometry if multiple draw calls select the same raw buffers.
                        long key = ((long)draw.vertexBufferIndex << 32) | (uint)draw.indexBufferIndex;
                        Mesh mesh;
                        if (!geometryCache.TryGetValue(key, out mesh))
                        {
                            try
                            {
                                mesh = BuildMesh(reader, document.vertices[(int)draw.vertexBufferIndex],
                                    document.indices[(int)draw.indexBufferIndex], "mesh_v" + draw.vertexBufferIndex + "_i" + draw.indexBufferIndex);
                                geometryCache.Add(key, mesh);
                                ctx.AddObjectToAsset("mesh_" + geometryCache.Count.ToString("D5"), mesh);
                            }
                            catch (Exception e)
                            {
                                ctx.LogImportWarning("Skipped invalid draw call " + modelIndex + "/" + drawIndex + ": " + e.Message);
                                continue;
                            }
                        }

                        var child = new GameObject("draw_" + drawIndex.ToString("D3") + "_mat_" + draw.materialIndex);
                        child.transform.SetParent(parent.transform, false);
                        var filter = child.AddComponent<MeshFilter>();
                        filter.sharedMesh = mesh;
                        var renderer = child.AddComponent<MeshRenderer>();

                        if (createPlaceholderMaterials)
                        {
                            Material mat;
                            int materialId = (int)draw.materialIndex;
                            if (!materials.TryGetValue(materialId, out mat))
                            {
                                Shader shader = Shader.Find("Universal Render Pipeline/Lit") ??
                                    Shader.Find("HDRP/Lit") ?? Shader.Find("Standard") ?? Shader.Find("Unlit/Color");
                                if (shader == null) throw new InvalidDataException("No supported material shader is available.");
                                mat = new Material(shader) { name = "mat_" + materialId.ToString("D4") };
                                if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", Color.white);
                                if (mat.HasProperty("_Color")) mat.SetColor("_Color", Color.white);
                                materials.Add(materialId, mat);
                                ctx.AddObjectToAsset("material_" + materialId.ToString("D4"), mat);
                            }
                            renderer.sharedMaterial = mat;
                        }
                        imported++;
                    }
                    if (imported >= meshLimit) break;
                }

                if (imported == 0)
                {
                    ctx.LogImportWarning("CAMDO parsed but no supported triangle meshes were found. " +
                        "Check the source format and use the legacy GLB viewer if needed.");
                }
                else
                {
                    ctx.LogImportWarning("Native geometry preview: " + imported + " draw calls, " +
                        geometryCache.Count + " unique meshes. Materials are placeholders; " +
                        "CASMT textures, rigs and animation are not decoded by this release.");
                }
            }
            catch (Exception e)
            {
                ctx.LogImportError("CAMDO native import failed for '" + ctx.assetPath + "': " + e.Message);
                root.name += " [Native Import Error]";
            }
            ctx.AddObjectToAsset("main", root);
            ctx.SetMainObject(root);
        }

        private Mesh BuildMesh(LegacyCamdoReader reader, LegacyCamdoReader.VertexBuffer buffer,
            LegacyCamdoReader.IndexBuffer indices, string name)
        {
            if (buffer.count < 3 || indices.count < 3 || indices.count % 3 != 0)
                throw new InvalidDataException("Unsupported index topology / empty mesh.");
            var pos = buffer.Find(0, 36);
            if (pos == null || pos.size < 12)
                throw new InvalidDataException("No Float32x3 position attribute.");
            var normal = buffer.Find(28, 32, 34, 37);
            var uv = buffer.Find(5, 6, 7, 8, 9, 10, 11, 12, 13);
            var color = buffer.Find(17, 35);
            var tangent = buffer.Find(29, 40);

            int count = buffer.count;
            var positions = new Vector3[count];
            Vector3[] normals = normal == null ? null : new Vector3[count];
            Vector2[] texCoords = uv == null ? null : new Vector2[count];
            Color32[] colors = color == null || !importVertexColors ? null : new Color32[count];
            Vector4[] tangents = tangent == null ? null : new Vector4[count];
            bool mirror = flipX;
            for (int i = 0; i < count; i++)
            {
                int baseOffset = checked(buffer.dataOffset + i * buffer.stride);
                var p = reader.BEVec3(baseOffset + pos.offset);
                positions[i] = new Vector3(mirror ? -p.x : p.x, p.y, p.z) * Mathf.Max(0.000001f, modelScale);

                if (normals != null)
                {
                    int a = baseOffset + normal.offset;
                    bool unorm = normal.type == 37;
                    float x = unorm ? reader.UNormSigned(a) : reader.SNorm(a);
                    float y = unorm ? reader.UNormSigned(a + 1) : reader.SNorm(a + 1);
                    float z = unorm ? reader.UNormSigned(a + 2) : reader.SNorm(a + 2);
                    normals[i] = new Vector3(mirror ? -x : x, y, z).normalized;
                }
                if (texCoords != null)
                {
                    float u = reader.BEFloat(baseOffset + uv.offset);
                    float v = reader.BEFloat(baseOffset + uv.offset + 4);
                    texCoords[i] = new Vector2(u, flipUVv ? 1f - v : v);
                }
                if (colors != null)
                {
                    int c = baseOffset + color.offset;
                    colors[i] = new Color32(reader.U8(c), reader.U8(c + 1),
                        reader.U8(c + 2), reader.U8(c + 3));
                }
                if (tangents != null)
                {
                    int t = baseOffset + tangent.offset;
                    bool unorm = tangent.type == 40;
                    float x = unorm ? reader.UNormSigned(t) : reader.SNorm(t);
                    float y = unorm ? reader.UNormSigned(t + 1) : reader.SNorm(t + 1);
                    float z = unorm ? reader.UNormSigned(t + 2) : reader.SNorm(t + 2);
                    float w = unorm ? reader.UNormSigned(t + 3) : reader.SNorm(t + 3);
                    tangents[i] = new Vector4(mirror ? -x : x, y, z, mirror ? -w : w);
                }
            }

            int[] triangles = new int[indices.count];
            bool invert = mirror ^ reverseFaces;
            for (int i = 0; i < triangles.Length; i += 3)
            {
                int a = reader.U16(indices.dataOffset + i * 2);
                int b = reader.U16(indices.dataOffset + (i + 1) * 2);
                int c = reader.U16(indices.dataOffset + (i + 2) * 2);
                if (a >= count || b >= count || c >= count)
                    throw new InvalidDataException("Triangle index exceeds vertex count at " + i);
                triangles[i] = a;
                triangles[i + 1] = invert ? c : b;
                triangles[i + 2] = invert ? b : c;
            }

            var result = new Mesh { name = name };
            if (count > 65535) result.indexFormat = IndexFormat.UInt32;
            result.vertices = positions;
            result.triangles = triangles;
            if (texCoords != null) result.uv = texCoords;
            if (colors != null) result.colors32 = colors;
            if (normals != null) result.normals = normals;
            else result.RecalculateNormals();
            if (tangents != null) result.tangents = tangents;
            result.RecalculateBounds();
            return result;
        }
    }

    // Format subset: MxmdLegacy 10040, Models, Model, Mesh, VertexData.
    // Wii U CAMDO MXMD v10040 stores BOTH its metadata/pointers/indices and
    // vertex attribute payloads in BIG-endian byte order. Unlike later Switch
    // files, this is not mixed-endian. Confirmed by xc3_lib/src/lib.rs
    // file_read_impl!(Endian::Big, mxmd::legacy::MxmdLegacy) and the
    // user's ma9990 logs (00 00 27 38 = 10040, not 942080000).
    internal sealed class LegacyCamdoReader
    {
        public sealed class VertexAttribute
        {
            public int type, size, offset;
        }
        public sealed class VertexBuffer
        {
            public int dataOffset, count, stride;
            public List<VertexAttribute> attributes = new List<VertexAttribute>();
            public VertexAttribute Find(params int[] types)
            {
                foreach (int type in types)
                    foreach (var attribute in attributes)
                        if (attribute.type == type) return attribute;
                return null;
            }
        }
        public sealed class IndexBuffer
        {
            public int dataOffset, count;
        }
        public sealed class Draw
        {
            public uint vertexBufferIndex, indexBufferIndex, materialIndex;
        }
        public sealed class Model
        {
            public List<Draw> meshes = new List<Draw>();
        }
        public sealed class Document
        {
            public List<Model> models = new List<Model>();
            public List<VertexBuffer> vertices = new List<VertexBuffer>();
            public List<IndexBuffer> indices = new List<IndexBuffer>();
        }

        private readonly byte[] bytes;
        public LegacyCamdoReader(byte[] data)
        {
            bytes = data ?? throw new ArgumentNullException(nameof(data));
            if (bytes.Length < 40 || bytes.Length > 512L * 1024 * 1024)
                throw new InvalidDataException("CAMDO source outside supported size limit (40 B–512 MiB).");
        }

        private void Range(long start, long count)
        {
            if (start < 0 || count < 0 || start > bytes.Length || count > bytes.Length - start)
                throw new InvalidDataException("Invalid CAMDO pointer/range: offset=" + start + " length=" + count +
                    " fileSize=" + bytes.Length);
        }
        public byte U8(int offset) { Range(offset, 1); return bytes[offset]; }
        // All integer metadata in Wii U MXMD is BIG-endian.
        // This includes the version at +4, all relative pointers/counts,
        // buffer descriptors, attribute type/size and u16 triangle indices.
        public ushort U16(int offset)
        {
            Range(offset, 2);
            return (ushort)((bytes[offset] << 8) | bytes[offset + 1]);
        }
        public uint U32(int offset)
        {
            Range(offset, 4);
            return ((uint)bytes[offset] << 24) | ((uint)bytes[offset + 1] << 16) |
                   ((uint)bytes[offset + 2] << 8) | bytes[offset + 3];
        }
        private int SafeInt(uint value, string label)
        {
            if (value > int.MaxValue) throw new InvalidDataException("Overflow in " + label);
            return (int)value;
        }
        private int Relative(int owner, int offset)
        {
            uint raw = U32(offset);
            if (raw == 0) return 0;
            long next = (long)owner + raw;
            Range(next, 1);
            return checked((int)next);
        }
        private int Count(int offset, int max, string label)
        {
            uint raw = U32(offset);
            if (raw > max) throw new InvalidDataException(label + " count exceeds maximum " + max + ": " + raw);
            return (int)raw;
        }
        public float BEFloat(int pos)
        {
            Range(pos, 4);
            if (BitConverter.IsLittleEndian)
            {
                byte[] data = { bytes[pos + 3], bytes[pos + 2], bytes[pos + 1], bytes[pos] };
                return BitConverter.ToSingle(data, 0);
            }
            return BitConverter.ToSingle(bytes, pos);
        }
        public Vector3 BEVec3(int offset)
        {
            return new Vector3(BEFloat(offset), BEFloat(offset + 4), BEFloat(offset + 8));
        }
        public float SNorm(int offset) { return Mathf.Max(-1f, unchecked((sbyte)U8(offset)) / 127f); }
        public float UNormSigned(int offset) { return U8(offset) / 127.5f - 1f; }

        public Document Parse()
        {
            if (U8(0) != (byte)'M' || U8(1) != (byte)'X' ||
                U8(2) != (byte)'M' || U8(3) != (byte)'D')
                throw new InvalidDataException("Not an MXMD header.");
            uint version = U32(4);
            if (version != 10040)
                throw new InvalidDataException("Unsupported MXMD version: " + version +
                    " (0x" + version.ToString("X8") + "; expected Wii U 10040 / 0x00002738).");

            int modelsBase = Relative(0, 8);
            int vertexBase = Relative(0, 20);
            if (modelsBase == 0 || vertexBase == 0)
                throw new InvalidDataException("Missing Models/VertexData root pointers.");

            var output = new Document();

            // Models starts with 6 float AABB values; then relative pointer/count.
            int modelArray = Relative(modelsBase, checked(modelsBase + 24));
            int modelCount = Count(checked(modelsBase + 28), 10000, "model");
            if (modelCount > 0 && modelArray == 0)
                throw new InvalidDataException("Nonzero model count has null pointer.");
            Range(modelArray, (long)modelCount * 68L);
            for (int modelIndex = 0; modelIndex < modelCount; modelIndex++)
            {
                // Model struct is 68 bytes; mesh array offsets are relative to Models base.
                int modelPos = checked(modelArray + modelIndex * 68);
                int meshArray = Relative(modelsBase, modelPos);
                int meshCount = Count(modelPos + 4, 20000, "mesh");
                if (meshCount > 0 && meshArray == 0)
                    throw new InvalidDataException("Mesh array pointer is null.");
                Range(meshArray, (long)meshCount * 64L);
                var model = new Model();
                for (int draw = 0; draw < meshCount; draw++)
                {
                    int meshPos = checked(meshArray + draw * 64);
                    model.meshes.Add(new Draw
                    {
                        vertexBufferIndex = U32(meshPos + 8),
                        indexBufferIndex = U32(meshPos + 12),
                        materialIndex = U32(meshPos + 20)
                    });
                }
                output.models.Add(model);
            }

            // VertexData: vertex buffer offset/count; index buffer offset/count.
            int vertexArray = Relative(vertexBase, vertexBase);
            int vertexCount = Count(vertexBase + 4, 30000, "vertex buffers");
            int indexArray = Relative(vertexBase, vertexBase + 8);
            int indexCount = Count(vertexBase + 12, 30000, "index buffers");
            if (vertexCount > 0 && vertexArray == 0 || indexCount > 0 && indexArray == 0)
                throw new InvalidDataException("Nonzero vertex/index buffer list has null pointer.");
            Range(vertexArray, (long)vertexCount * 24L);
            Range(indexArray, (long)indexCount * 12L);

            for (int i = 0; i < vertexCount; i++)
            {
                int pos = checked(vertexArray + i * 24);
                int data = Relative(vertexBase, pos);
                int count = Count(pos + 4, 2000000, "vertices");
                int stride = Count(pos + 8, 1024, "vertex stride");
                int attrArray = Relative(vertexBase, pos + 12);
                int attrCount = Count(pos + 16, 256, "attributes");
                if (count > 0 && (data == 0 || stride == 0))
                    throw new InvalidDataException("Nonempty vertex buffer with missing data/stride.");
                if (attrCount > 0 && attrArray == 0)
                    throw new InvalidDataException("Nonempty attribute list with null pointer.");
                Range(data, (long)count * stride);
                Range(attrArray, (long)attrCount * 4L);
                var buffer = new VertexBuffer { dataOffset = data, count = count, stride = stride };
                int attrOffset = 0;
                for (int j = 0; j < attrCount; j++)
                {
                    int a = checked(attrArray + j * 4);
                    int type = U16(a);
                    int size = U16(a + 2);
                    if (size <= 0 || attrOffset + size > stride)
                        throw new InvalidDataException("Vertex attribute size exceeds stride (buffer " + i + ").");
                    buffer.attributes.Add(new VertexAttribute { type = type, size = size, offset = attrOffset });
                    attrOffset += size;
                }
                output.vertices.Add(buffer);
            }

            for (int i = 0; i < indexCount; i++)
            {
                int pos = checked(indexArray + i * 12);
                int offset = Relative(vertexBase, pos);
                int count = Count(pos + 4, 6000000, "indices");
                if (count > 0 && offset == 0)
                    throw new InvalidDataException("Nonempty index buffer with null pointer.");
                Range(offset, (long)count * 2L);
                output.indices.Add(new IndexBuffer { dataOffset = offset, count = count });
            }
            return output;
        }
    }
}
#endif
