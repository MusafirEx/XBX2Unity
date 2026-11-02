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
    [ScriptedImporter(4, "camdo")]
    public sealed class XBXCamdoNativeImporter : ScriptedImporter
    {
        [Header("Model")]
        [Min(0.000001f)] public float modelScale = 1f;
        public bool flipX = true;
        public bool reverseFaces = false;
        public bool flipUVv = false;
        public bool importVertexColors = true;
        [Header("Skell HKT Skeleton Preview")]
        public bool importSkellHktHierarchy = true;
        [Header("Material")]
        public bool createPlaceholderMaterials = true;
        [Header("Extracted Map Textures")]
        public bool bindExtractedTextures = true;
        [Range(0, 15)] public int baseTextureSlot = 0;
        [Header("Experimental Map Edge Blend")]
        // Explicit opt-in until the source shader's texture roles are verified.
        public bool enableExperimentalEdgeBlend = false;
        [Range(0, 15)] public int blendTextureSlot = 1;
        [Range(0, 3)] public int blendChannel = 0;
        // 0=vBlend; 1=vertex color; 2=base texture RGBA; 3=overlay texture RGBA; 4=constant.
        [Range(0, 4)] public int blendMaskSource = 0;
        // Optional second UV set for the overlay texture. vBlend remains in uv3.
        [Range(0, 1)] public int overlayUVSet = 0;
        public bool invertBlendMask = false;
        // 0=composite; 1=raw mask; 2=blend weight; 3=base; 4=overlay; 5=vBlend RGB.
        [Range(0, 5)] public int blendDebugView = 0;
        [Range(0f, 1f)] public float constantBlendWeight = 0.5f;
        [Range(0f, 1f)] public float blendThreshold = 0.5f;
        [Range(0.001f, 0.5f)] public float blendSoftness = 0.12f;
        public bool useVertexTint = false;

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
                var document = reader.Parse(File.Exists(companion) ? companion : null);

                // Skell models have a sibling *_rig.hkt Havok skeleton. Build the
                // native transform hierarchy, but do NOT claim skinned renderers
                // or animation tracks until bone palettes/weights are decoded.
                string rigPath = Path.Combine(
                    Path.GetDirectoryName(ctx.assetPath) ?? string.Empty,
                    Path.GetFileNameWithoutExtension(ctx.assetPath) + "_rig.hkt");
                if (importSkellHktHierarchy && File.Exists(rigPath))
                {
                    ctx.DependsOnSourceAsset(rigPath);
                    try
                    {
                        var hkt = XBXSkellHktRig.Read(rigPath);
                        var rigRoot = XBXSkellHktRig.Build(hkt, flipX, modelScale);
                        rigRoot.transform.SetParent(root.transform, false);
                        ctx.LogImportWarning("Skell HKT skeleton hierarchy: " +
                            hkt.bones.Count + " bones imported from " + rigPath +
                            ". Geometry is still STATIC: no verified bone weights/bindposes " +
                            "or motion tracks have been applied.");
                    }
                    catch (Exception rigError)
                    {
                        ctx.LogImportWarning("Skell HKT hierarchy preview could not be built: " +
                            rigError.Message + ". Static CAMDO geometry is unaffected.");
                    }
                }

                Dictionary<int, Material> materials = new Dictionary<int, Material>();
                int boundBaseTextures = 0, boundBlendTextures = 0;
                int meshHasVBlend = 0, meshHasVertexColor = 0, meshHasUv1 = 0, meshNoVBlend = 0;
                int distinctTexturePairs = 0;
                int missingTextureReferences = 0;
                string textureRoot = FindMapTextureRoot(ctx.assetPath);
                if (!string.IsNullOrEmpty(document.textureMetadataWarning))
                    ctx.LogImportWarning("CAMDO texture metadata: " + document.textureMetadataWarning);
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
                        if (sourceModel.geometry == null ||
                            draw.vertexBufferIndex >= sourceModel.geometry.vertices.Count ||
                            draw.indexBufferIndex >= sourceModel.geometry.indices.Count)
                        {
                            ctx.LogImportWarning("Invalid buffer index in model " + modelIndex + ", mesh " + drawIndex);
                            continue;
                        }

                        // Reuse geometry if multiple draw calls select the same raw buffers.
                        long key = ((long)modelIndex << 40) |
                            ((long)draw.vertexBufferIndex << 20) | (uint)draw.indexBufferIndex;
                        Mesh mesh;
                        if (!geometryCache.TryGetValue(key, out mesh))
                        {
                            try
                            {
                                var activeBuffer = sourceModel.geometry.vertices[(int)draw.vertexBufferIndex];
                                if (activeBuffer.Find(14) != null) meshHasVBlend++;
                                else meshNoVBlend++;
                                if (activeBuffer.Find(17, 35) != null) meshHasVertexColor++;
                                if (activeBuffer.Find(6) != null) meshHasUv1++;
                                mesh = BuildMesh(sourceModel.geometry.reader,
                                    sourceModel.geometry.vertices[(int)draw.vertexBufferIndex],
                                    sourceModel.geometry.indices[(int)draw.indexBufferIndex],
                                    "mesh_g" + modelIndex + "_v" + draw.vertexBufferIndex + "_i" + draw.indexBufferIndex);
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
                                // Shader.Find can return null in an AssetImportWorker
                                // before custom shader registration. Load by package path
                                // first for UPM; Shader.Find remains the .unitypackage fallback.
                                Shader shader = null;
                                if (enableExperimentalEdgeBlend)
                                {
                                    const string shaderPath =
                                        "Packages/com.musafirex.xbx2unity/Runtime/Shaders/XBXMapVertexBlend.shader";
                                    shader = AssetDatabase.LoadAssetAtPath<Shader>(shaderPath) ??
                                        Shader.Find("XBX2Unity/Map Vertex Blend");
                                    if (shader == null)
                                        ctx.LogImportWarning("Map Vertex Blend shader not loaded; " +
                                            "falling back to Lit. Check that the Runtime/Shaders folder is installed.");
                                    else if (shaderPath.StartsWith("Packages/") &&
                                        AssetDatabase.LoadAssetAtPath<Shader>(shaderPath) != null)
                                        ctx.DependsOnSourceAsset(shaderPath);
                                }
                                if (shader == null)
                                    shader = Shader.Find("Universal Render Pipeline/Lit") ??
                                        Shader.Find("HDRP/Lit") ?? Shader.Find("Standard") ?? Shader.Find("Unlit/Texture");
                                if (shader == null) throw new InvalidDataException("No supported material shader is available.");
                                mat = new Material(shader) { name = "mat_" + materialId.ToString("D4") };
                                if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", Color.white);
                                if (mat.HasProperty("_Color")) mat.SetColor("_Color", Color.white);
                                if (mat.HasProperty("_BlendChannel")) mat.SetFloat("_BlendChannel", blendChannel);
                                if (mat.HasProperty("_BlendThreshold")) mat.SetFloat("_BlendThreshold", blendThreshold);
                                if (mat.HasProperty("_BlendSoftness")) mat.SetFloat("_BlendSoftness", blendSoftness);
                                if (mat.HasProperty("_MaskSource")) mat.SetFloat("_MaskSource", blendMaskSource);
                                if (mat.HasProperty("_ConstantWeight")) mat.SetFloat("_ConstantWeight", constantBlendWeight);
                                if (mat.HasProperty("_OverlayUVSet")) mat.SetFloat("_OverlayUVSet", overlayUVSet);
                                if (mat.HasProperty("_InvertMask")) mat.SetFloat("_InvertMask", invertBlendMask ? 1f : 0f);
                                if (mat.HasProperty("_DebugView")) mat.SetFloat("_DebugView", blendDebugView);
                                if (mat.HasProperty("_UseVertexTint")) mat.SetFloat("_UseVertexTint", useVertexTint ? 1f : 0f);
                                if (mat.HasProperty("_EnableBlend")) mat.SetFloat("_EnableBlend", 0f);

                                if (bindExtractedTextures && textureRoot != null)
                                {
                                    var baseTexture = ResolveTexture(ctx, document, materialId,
                                        baseTextureSlot, textureRoot, ref missingTextureReferences);
                                    if (baseTexture != null)
                                    {
                                        if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", baseTexture);
                                        if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", baseTexture);
                                        boundBaseTextures++;
                                    }
                                    // Slot 1 might be a normal map or shader lookup. Never
                                    // turn on blending without the explicit opt-in toggle.
                                    if (enableExperimentalEdgeBlend && shader.name == "XBX2Unity/Map Vertex Blend")
                                    {
                                        var blendTexture = ResolveTexture(ctx, document, materialId,
                                            blendTextureSlot, textureRoot, ref missingTextureReferences);
                                        if (baseTexture != null && blendTexture != null)
                                        {
                                            mat.SetTexture("_BlendTex", blendTexture);
                                            mat.SetFloat("_EnableBlend", 1f);
                                            boundBlendTextures++;
                                            if (baseTexture != blendTexture) distinctTexturePairs++;
                                        }
                                    }
                                }
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
                    ctx.LogImportWarning("CAMDO model metadata parsed but no supported triangle meshes were imported. " +
                        "Check CAMDO/CASMT buffer lookup indices and the native importer warnings.");
                }
                else
                {
                    ctx.LogImportWarning("Native geometry preview: " + imported + " draw calls, " +
                        geometryCache.Count + " unique meshes. Bound extracted base textures: " +
                        boundBaseTextures + ", experimental blended materials: " + boundBlendTextures +
                        ", unresolved texture references: " + missingTextureReferences +
                        ". vBlend buffers=" + meshHasVBlend +
                        ", without vBlend=" + meshNoVBlend +
                        ", vertex color buffers=" + meshHasVertexColor +
                        ", UV1 buffers=" + meshHasUv1 +
                        ", distinct base/overlay texture pairs=" + distinctTexturePairs +
                        ". Edge shader uses selected mask source; source texture roles are not verified.");
                    if (enableExperimentalEdgeBlend && boundBlendTextures == 0)
                        ctx.LogImportWarning("Edge blending is enabled but no materials have two resolved textures. " +
                            "Check Blend Texture Slot, mapped PNG paths, and the source material links.");
                    else if (enableExperimentalEdgeBlend && blendMaskSource == 0 && meshHasVBlend == 0)
                        ctx.LogImportWarning("Edge blending uses vBlend but no imported mesh has attribute 14. " +
                            "Inspect the mask source (vertex color/texture alpha) before using a fallback.");
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

        // Exact file-layout mapping from casmExtract, not basename guessing:
        // textures/<container>/<four digit id>.png for cached terrain,
        // textures/<four digit id>.png for uncached object textures.
        private static string FindMapTextureRoot(string assetPath)
        {
            string folder = Path.GetDirectoryName(assetPath).Replace('\\', '/');
            for (int i = 0; i < 4 && !string.IsNullOrEmpty(folder); i++)
            {
                string candidate = folder + "/textures";
                if (AssetDatabase.IsValidFolder(candidate)) return candidate;
                folder = Path.GetDirectoryName(folder)?.Replace('\\', '/');
            }
            return null;
        }

        private static Texture2D ResolveTexture(AssetImportContext ctx, LegacyCamdoReader.Document doc,
            int materialId, int slot, string textureRoot, ref int missing)
        {
            if (materialId < 0 || materialId >= doc.materials.Count) return null;
            var material = doc.materials[materialId];
            if (slot < 0 || slot >= material.textureIndices.Count) return null;
            int id = material.textureIndices[slot];
            if (id < 0 || id >= doc.externalTextures.Count) { missing++; return null; }
            var ext = doc.externalTextures[id];
            int fileId;
            string relative;
            if (ext.externalId < 0)
            {
                // Cached: textureID indexes a texture inside a cached container.
                if (ext.containerId < 0 || ext.textureId < 0) { missing++; return null; }
                fileId = ext.textureId;
                relative = ext.containerId + "/" + fileId.ToString("D4");
            }
            else
            {
                // Uncached: externalTextureID addresses the global textures list.
                fileId = ext.externalId;
                relative = fileId.ToString("D4");
            }

            foreach (string suffix in new[] { ".png", ".dds", ".tga" })
            {
                string asset = textureRoot + "/" + relative + suffix;
                Texture2D tex = AssetDatabase.LoadAssetAtPath<Texture2D>(asset);
                if (tex == null) continue;
                ctx.DependsOnSourceAsset(asset);
                return tex;
            }
            missing++;
            return null;
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
            var blend = buffer.Find(14); // vBlend = UNorm8x4, independent of vColor.
            var uv1 = buffer.Find(6); // distinct second UV set; not the blend mask.
            var tangent = buffer.Find(29, 40);

            int count = buffer.count;
            var positions = new Vector3[count];
            Vector3[] normals = normal == null ? null : new Vector3[count];
            Vector2[] texCoords = uv == null ? null : new Vector2[count];
            Vector2[] overlayTexCoords = uv1 == null || uv1.size < 8 ? null : new Vector2[count];
            Color32[] colors = color == null || !importVertexColors ? null : new Color32[count];
            var blendWeights = blend == null || blend.size < 4 ? null : new List<Vector4>(count);
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
                if (overlayTexCoords != null)
                {
                    float u1 = reader.BEFloat(baseOffset + uv1.offset);
                    float v1 = reader.BEFloat(baseOffset + uv1.offset + 4);
                    overlayTexCoords[i] = new Vector2(u1, flipUVv ? 1f - v1 : v1);
                }
                if (colors != null)
                {
                    int c = baseOffset + color.offset;
                    colors[i] = new Color32(reader.U8(c), reader.U8(c + 1),
                        reader.U8(c + 2), reader.U8(c + 3));
                }
                if (blendWeights != null)
                {
                    int b = baseOffset + blend.offset;
                    blendWeights.Add(new Vector4(reader.U8(b) / 255f,
                        reader.U8(b + 1) / 255f, reader.U8(b + 2) / 255f,
                        reader.U8(b + 3) / 255f));
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
            if (overlayTexCoords != null) result.uv2 = overlayTexCoords;
            if (colors != null) result.colors32 = colors;
            if (blendWeights != null) result.SetUVs(2, blendWeights);
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
        public sealed class Geometry
        {
            public LegacyCamdoReader reader;
            public List<VertexBuffer> vertices = new List<VertexBuffer>();
            public List<IndexBuffer> indices = new List<IndexBuffer>();
        }
        public sealed class Model
        {
            public List<Draw> meshes = new List<Draw>();
            public Geometry geometry;
        }
        public sealed class MaterialInfo
        {
            public List<int> textureIndices = new List<int>();
        }
        public sealed class ExternalTexture
        {
            public int textureId, containerId, externalId;
        }
        public sealed class Document
        {
            public List<Model> models = new List<Model>();
            public List<MaterialInfo> materials = new List<MaterialInfo>();
            public List<ExternalTexture> externalTextures = new List<ExternalTexture>();
            public string textureMetadataWarning;
        }

        private readonly byte[] bytes;
        public LegacyCamdoReader(byte[] data, bool isExternal = false)
        {
            bytes = data ?? throw new ArgumentNullException(nameof(data));
            if (bytes.Length < (isExternal ? 48 : 68) || bytes.Length > 512L * 1024 * 1024)
                throw new InvalidDataException((isExternal ? "CASMT" : "CAMDO") +
                    " source outside supported geometry read size (48/68 B–512 MiB).");
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
        // Safe bounded hexadecimal report; useful when a CAMDO is empty, stripped or
        // uses an unexpected variant. No geometry decoding is attempted here.
        public string HeaderAudit()
        {
            var result = new System.Text.StringBuilder();
            result.Append("len=").Append(bytes.Length);
            result.Append(" first64=");
            int length = Math.Min(64, bytes.Length);
            for (int i = 0; i < length; i++)
            {
                if (i != 0) result.Append(' ');
                result.Append(bytes[i].ToString("X2"));
            }
            result.Append(" fields=[");
            for (int p = 4; p < Math.Min(64, bytes.Length) - 3; p += 4)
            {
                if (p != 4) result.Append(", ");
                result.Append("0x").Append(p.ToString("X2")).Append(":0x")
                    .Append(U32(p).ToString("X8"));
            }
            result.Append(']');
            return result.ToString();
        }

        public float SNorm(int offset) { return Mathf.Max(-1f, unchecked((sbyte)U8(offset)) / 127f); }
        public float UNormSigned(int offset) { return U8(offset) / 127.5f - 1f; }

        public Document Parse(string externalPath)
        {
            if (U8(0) != (byte)'M' || U8(1) != (byte)'X' ||
                U8(2) != (byte)'M' || U8(3) != (byte)'D')
                throw new InvalidDataException("Not an MXMD header.");
            uint version = U32(4);
            if (version != 10040)
                throw new InvalidDataException("Unsupported MXMD version: " + version +
                    " (expected Wii U big-endian 10040).");

            uint modelsRaw = U32(0x08);
            uint vertexRaw = U32(0x14);
            uint externalIdsRaw = U32(0x28);
            int externalIdsCount = unchecked((int)U32(0x2C));
            if (modelsRaw == 0)
                throw new InvalidDataException("Missing Models root pointer at 0x08; " + HeaderAudit());
            int modelsBase = Relative(0, 0x08);
            var output = new Document();

            // MXMD::V1::Model has 24 bytes of bounds followed by a
            // base-relative model/mesh-group offset+count at +0x18.
            int modelArray = Relative(modelsBase, checked(modelsBase + 24));
            int modelCount = Count(modelsBase + 28, 10000, "model");
            if (modelCount > 0 && modelArray == 0)
                throw new InvalidDataException("Nonzero model count has null pointer.");
            Range(modelArray, (long)modelCount * 68L);
            for (int modelIndex = 0; modelIndex < modelCount; modelIndex++)
            {
                int modelPos = checked(modelArray + modelIndex * 68);
                int meshArray = Relative(modelsBase, modelPos);
                int meshCount = Count(modelPos + 4, 20000, "mesh");
                if (meshCount > 0 && meshArray == 0)
                    throw new InvalidDataException("Mesh array pointer is null for group " + modelIndex);
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

            // Material and texture metadata should never break a valid geometry
            // import. Invalid preview links fall back to the grey material.
            try { ParseTextureMetadata(output); }
            catch (InvalidDataException e) { output.textureMetadataWarning = e.Message; }

            if (vertexRaw != 0)
            {
                // Character/Skybox CAMDO: geometry embedded in the CAMDO.
                var internalGeometry = ParseGeometry(Relative(0, 0x14));
                foreach (var model in output.models) model.geometry = internalGeometry;
                return output;
            }

            // IMPORTANT: Map CAMDO from casmExtract intentionally has a zero
            // embedded vertex pointer. External buffer lookup is 0x28 and
            // count/format sentinel 0x2c; geometry lives in companion CASMT.
            // See XenoToolset/casmExtract and XenoLib/obsolete/MXMD.cpp.
            if (externalIdsRaw == 0)
                throw new InvalidDataException("No embedded or external geometry pointer. " +
                    "VertexData[0x14]=0, ExternalBufferIDs[0x28]=0; " + HeaderAudit());
            if (string.IsNullOrEmpty(externalPath) || !File.Exists(externalPath))
                throw new FileNotFoundException("Map CAMDO uses external geometry. " +
                    "The corresponding .casmt file must be next to the .camdo file.", externalPath);

            long length = new FileInfo(externalPath).Length;
            if (length < 48 || length > 512L * 1024 * 1024)
                throw new InvalidDataException("External CASMT geometry size " + length +
                    " exceeds the current 512 MiB in-memory import limit. " +
                    "This importer does not fabricate a missing vertex buffer.");
            var externalReader = new LegacyCamdoReader(File.ReadAllBytes(externalPath), true);
            int table = Relative(0, 0x28);
            var cache = new Dictionary<int, Geometry>();

            if (externalIdsCount == -1)
            {
                // Terrain: header [lookups offset,count, groupIDs offset,count,
                // unknown, six unused fields]. Each lookup has 6 floats,
                // two CASMT buffer offsets and 1 unknown = 36 bytes.
                Range(table, 44);
                int lookupOffset = Relative(table, table);
                int lookupCount = Count(table + 4, 50000, "terrain buffer pairs");
                int groupOffset = Relative(table, table + 8);
                int groupCount = Count(table + 12, 50000, "terrain group indices");
                Range(lookupOffset, (long)lookupCount * 36);
                Range(groupOffset, (long)groupCount * 2);
                if (groupCount < modelCount)
                    throw new InvalidDataException("Terrain buffer group indices (" + groupCount +
                        ") fewer than model groups (" + modelCount + ").");

                for (int i = 0; i < modelCount; i++)
                {
                    int groupId = U16(groupOffset + i * 2);
                    if (lookupCount == 0 || groupId >= lookupCount * 2)
                        throw new InvalidDataException("Terrain group " + i +
                            " has invalid external geometry ID " + groupId +
                            " for " + lookupCount + " pairs.");
                    int pairId = groupId % lookupCount;
                    int side = groupId / lookupCount;
                    int pairAddr = checked(lookupOffset + pairId * 36 + 24 + side * 4);
                    int geometryOffset = SafeInt(U32(pairAddr), "terrain CASMT offset");
                    if (!cache.TryGetValue(geometryOffset, out var geometry))
                    {
                        geometry = externalReader.ParseGeometry(geometryOffset);
                        cache.Add(geometryOffset, geometry);
                    }
                    output.models[i].geometry = geometry;
                }
            }
            else if (externalIdsCount > 0 && externalIdsCount <= 50000)
            {
                // Objects: flat table of CASMT offsets, one per group.
                Range(table, (long)externalIdsCount * 4);
                if (externalIdsCount < modelCount)
                    throw new InvalidDataException("External object geometry count " +
                        externalIdsCount + " < model groups " + modelCount);
                for (int i = 0; i < modelCount; i++)
                {
                    int geometryOffset = SafeInt(U32(table + i * 4), "object CASMT offset");
                    if (!cache.TryGetValue(geometryOffset, out var geometry))
                    {
                        geometry = externalReader.ParseGeometry(geometryOffset);
                        cache.Add(geometryOffset, geometry);
                    }
                    output.models[i].geometry = geometry;
                }
            }
            else
            {
                throw new InvalidDataException("Unsupported external geometry index format: " +
                    externalIdsCount + " at 0x2C (expected terrain -1 or object count >0).");
            }

            return output;
        }

        private void ParseTextureMetadata(Document document)
        {
            uint materialPointer = U32(0x0C);
            if (materialPointer != 0)
            {
                int materialsBase = Relative(0, 0x0C);
                int materialsArray = Relative(materialsBase, materialsBase);
                int materialCount = Count(materialsBase + 4, 8192, "materials");
                if (materialCount > 0 && materialsArray == 0)
                    throw new InvalidDataException("Material table pointer is missing");
                // MXMDMaterial_V1 is 136 bytes, NOT 132:
                // name/flags(8) + floats[13](52) + texture offset/count(8)
                // + unk[9](36) + links offset/count(8) + null[6](24).
                // Source: PredatorCZ/XenoLib/source/MXMD_V1.h.
                // Using 132 shifts every subsequent material into its floats,
                // producing bogus pointers like 0x3F000000 in Unity Editor.log.
                const int MaterialStride = 136;
                Range(materialsArray, (long)materialCount * MaterialStride);
                for (int i = 0; i < materialCount; i++)
                {
                    int materialAt = checked(materialsArray + i * MaterialStride);
                    uint linkRaw = U32(materialAt + 60);
                    int linkCount = Count(materialAt + 64, 256, "material texture links");
                    if (linkCount > 0 && (linkRaw == 0 ||
                        (long)materialsBase + linkRaw > bytes.Length - (long)linkCount * 4))
                    {
                        throw new InvalidDataException("Invalid material texture pointer: material=" + i +
                            ", record=0x" + materialAt.ToString("X") +
                            ", base=0x" + materialsBase.ToString("X") +
                            ", texturesOffset=0x" + linkRaw.ToString("X8") +
                            ", texturesCount=" + linkCount +
                            ", arrayCount=" + materialCount +
                            ", fileSize=" + bytes.Length);
                    }
                    int linkArray = linkRaw == 0 ? 0 : Relative(materialsBase, materialAt + 60);
                    if (linkCount > 0 && linkArray == 0)
                        throw new InvalidDataException("Material " + i + " link table is missing");
                    Range(linkArray, (long)linkCount * 4);
                    var material = new MaterialInfo();
                    for (int j = 0; j < linkCount; j++)
                        material.textureIndices.Add(U16(linkArray + j * 4));
                    document.materials.Add(material);
                }
            }

            uint externalPtr = U32(0x34);
            int externalCount = Count(0x38, 65536, "external texture records");
            if (externalCount == 0) return;
            if (externalPtr == 0) throw new InvalidDataException("External texture array pointer is null");
            int records = Relative(0, 0x34);
            // casmExtract writes records of 4 x signed 16-bit values:
            // textureID, resolved containerID, externalTextureID, unknown.
            Range(records, (long)externalCount * 8);
            for (int i = 0; i < externalCount; i++)
            {
                int at = checked(records + i * 8);
                document.externalTextures.Add(new ExternalTexture
                {
                    textureId = unchecked((short)U16(at)),
                    containerId = unchecked((short)U16(at + 2)),
                    externalId = unchecked((short)U16(at + 4))
                });
            }
        }

        private Geometry ParseGeometry(int vertexBase)
        {
            // MXMDGeometryHeader_V1 holds two base-relative arrays:
            // 24-byte VertexBufferDescriptor and 12-byte IndexBufferDescriptor.
            // The base is the geometry header, which may start inside CASMT.
            Range(vertexBase, 48);
            int vertexArray = Relative(vertexBase, vertexBase);
            int vertexCount = Count(vertexBase + 4, 30000, "vertex buffers");
            int indexArray = Relative(vertexBase, vertexBase + 8);
            int indexCount = Count(vertexBase + 12, 30000, "index buffers");
            if ((vertexCount > 0 && vertexArray == 0) ||
                (indexCount > 0 && indexArray == 0))
                throw new InvalidDataException("Nonzero vertex/index buffer count has null pointer.");
            Range(vertexArray, (long)vertexCount * 24);
            Range(indexArray, (long)indexCount * 12);
            var result = new Geometry { reader = this };

            for (int i = 0; i < vertexCount; i++)
            {
                int p = checked(vertexArray + i * 24);
                int data = Relative(vertexBase, p);
                int count = Count(p + 4, 2000000, "vertices");
                int stride = Count(p + 8, 1024, "vertex stride");
                int attrArray = Relative(vertexBase, p + 12);
                int attrCount = Count(p + 16, 256, "attributes");
                if (count > 0 && (data == 0 || stride == 0))
                    throw new InvalidDataException("Nonempty vertex buffer with missing data/stride.");
                if (attrCount > 0 && attrArray == 0)
                    throw new InvalidDataException("Nonempty vertex descriptors with null pointer.");
                Range(data, (long)count * stride);
                Range(attrArray, (long)attrCount * 4);
                var vb = new VertexBuffer { dataOffset = data, count = count, stride = stride };
                int attrOffset = 0;
                for (int j = 0; j < attrCount; j++)
                {
                    int at = checked(attrArray + j * 4);
                    int type = U16(at);
                    int size = U16(at + 2);
                    if (size == 0 || attrOffset + size > stride)
                        throw new InvalidDataException("Invalid vertex attribute size/stride in buffer " + i);
                    vb.attributes.Add(new VertexAttribute { type = type, size = size, offset = attrOffset });
                    attrOffset += size;
                }
                result.vertices.Add(vb);
            }

            for (int i = 0; i < indexCount; i++)
            {
                int p = checked(indexArray + i * 12);
                int data = Relative(vertexBase, p);
                int count = Count(p + 4, 6000000, "indices");
                if (count > 0 && data == 0)
                    throw new InvalidDataException("Nonempty triangle indices with null data pointer.");
                Range(data, (long)count * 2);
                result.indices.Add(new IndexBuffer { dataOffset = data, count = count });
            }
            return result;
        }
    }
}
#endif
