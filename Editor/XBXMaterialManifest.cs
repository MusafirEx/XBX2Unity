// Stable, source-indexed material metadata for a native CAMDO import.
// Inspired by the material manifests in Bress2Unity and Unity-CnC-SAGE-Tools.
// Data is read-only; external overrides are serialized on the importer.
#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;

namespace MusafirEx.XBX2Unity.Editor
{
    [Serializable]
    public sealed class XBXMaterialTextureSlot
    {
        public int slot;
        public int textureId;
        public string sourceName;
        public string usageHint;
        public string decodedFormat;
        public Texture2D decodedTexture;
    }

    [Serializable]
    public sealed class XBXMaterialEntry
    {
        // MXMD material index, NOT a guessed name, persists across reimport.
        public int materialIndex;
        public string sourceName;
        public int rendererCount;
        public Color sourceColor = Color.white;
        public int chosenAlbedoSlot = -1;
        public string bindingStatus;
        public Material generatedMaterial;
        public Material assignedMaterial;
        public List<XBXMaterialTextureSlot> textureSlots = new List<XBXMaterialTextureSlot>();
    }

    [Serializable]
    public sealed class XBXMaterialOverride
    {
        public int materialIndex;
        public Material material;
    }

    // The importer registers this as the 'xbx_material_manifest' subasset.
    // It records source links without mutating the original CAMDO/CASMT.
    public sealed class XBXMaterialManifest : ScriptableObject
    {
        public List<XBXMaterialEntry> entries = new List<XBXMaterialEntry>();
    }
}
#endif
