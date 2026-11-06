// Conservative MXMD MTXT semantic role discovery for Skell materials.
// Role names inferred from actual source names in dl019100/dl019600/dl109100.
// _RGB is a COMPANION mask (user-observed: R=metallic, G=body, B=glow).
// It is NOT proven as a diffuse/albedo image or a 4-zone RGBA paint mask.
// Real channel semantics should still be confirmed per Skell/material family.
#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;

namespace MusafirEx.XBX2Unity.Editor
{
    internal static class XBXSkellMaterialLayers
    {
        internal enum Role { Normal, Specular, Reflection, Glow, RgbCompanion }

        internal struct Match
        {
            internal int slot;
            internal int textureId;
            internal XBXSkellMtxt.Record texture;
            internal bool IsValid { get { return texture != null && texture.texture != null; } }
        }

        internal static Match Find(List<int> sourceTextureIds,
            Dictionary<int, XBXSkellMtxt.Record> decoded, Role role)
        {
            var match = new Match { slot = -1, textureId = -1 };
            if (sourceTextureIds == null || decoded == null) return match;
            for (int slot = 0; slot < sourceTextureIds.Count; slot++)
            {
                int id = sourceTextureIds[slot];
                if (!decoded.TryGetValue(id, out var record) ||
                    record == null || record.texture == null) continue;
                if (!IsRole(record, role)) continue;
                match.slot = slot;
                match.textureId = id;
                match.texture = record;
                return match;
            }
            return match;
        }

        internal static string Describe(XBXSkellMtxt.Record record)
        {
            if (record == null) return "Missing MTXT";
            if (IsRole(record, Role.Normal)) return "Normal candidate (BC5)";
            if (IsRole(record, Role.Specular)) return "Specular candidate (SPM)";
            if (IsRole(record, Role.Reflection)) return "Reflection candidate (RFM/REF)";
            if (IsRole(record, Role.Glow)) return "Glow candidate (GLO/GLW)";
            if (IsRole(record, Role.RgbCompanion))
                return "RGB companion mask (R metallic / G body / B glow, observed)";
            return record.ColorScore >= 0 ? "Color / diffuse candidate" : "Unclassified source data";
        }

        private static bool Token(string n, string role)
        {
            if (string.IsNullOrEmpty(n)) return false;
            string[] parts = n.ToUpperInvariant().Split('_');
            foreach (string part in parts)
                if (part == role) return true;
            return false;
        }

        internal static bool IsRole(XBXSkellMtxt.Record r, Role role)
        {
            if (r == null) return false;
            string n = (r.sourceName ?? "").ToUpperInvariant();
            switch (role)
            {
                case Role.Normal:
                    return r.format == 53 && r.IsNormalData;
                case Role.Specular:
                    return r.format == 49 && Token(n, "SPM");
                case Role.Reflection:
                    return r.format == 49 &&
                        (Token(n, "RFM") || Token(n, "REF") || Token(n, "RF"));
                case Role.Glow:
                    return r.format == 49 && (Token(n, "GLO") || Token(n, "GLW"));
                case Role.RgbCompanion:
                    return r.format == 49 && n.EndsWith("_RGB", StringComparison.Ordinal);
                default: return false;
            }
        }

        // For opt-in native preview material binding only. A separate external
        // .mat override is never modified by the CAMDO importer.
        internal static bool AssignNormal(Material material, Match source)
        {
            if (!source.IsValid || material == null) return false;
            bool bound = false;
            if (material.HasProperty("_BumpMap"))
            {
                material.SetTexture("_BumpMap", source.texture.texture);
                bound = true;
            }
            if (material.HasProperty("_NormalMap"))
            {
                material.SetTexture("_NormalMap", source.texture.texture);
                bound = true;
            }
            if (bound)
            {
                if (material.HasProperty("_BumpScale")) material.SetFloat("_BumpScale", 1f);
                material.EnableKeyword("_NORMALMAP");
            }
            return bound;
        }

        internal static bool AssignSpecular(Material material, Match source)
        {
            if (!source.IsValid || material == null ||
                !material.HasProperty("_SpecGlossMap")) return false;
            material.SetTexture("_SpecGlossMap", source.texture.texture);
            // Shader keyword on supported Specular setup is activated by
            // explicit opt-in. Do not force metallic/reflective shader modes
            // for arbitrary user shaders.
            if (material.shader != null &&
                material.shader.name == "XBX2Unity/Skell Layer Preview")
                material.SetFloat("_UseSpecularMap", 1f);
            else
            {
                if (material.HasProperty("_WorkflowMode"))
                {
                    // URP Lit: 0 = specular workflow, 1 = metallic.
                    material.SetFloat("_WorkflowMode", 0f);
                    material.EnableKeyword("_SPECULAR_SETUP");
                }
                material.EnableKeyword("_SPECGLOSSMAP");
            }
            return true;
        }
    }
}
#endif
