using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>Only configures ChenHaoyu's Ruin Courtyard art. Does not rebuild any game content.</summary>
public sealed class RuinV08ModelImporter : AssetPostprocessor
{
    private const string Root = "Assets/Game/Resources/RuinCourtyard/";
    public override uint GetVersion() => 13;

    private void OnPreprocessModel()
    {
        if (!assetPath.StartsWith(Root, StringComparison.Ordinal) ||
            !assetPath.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase)) return;
        var importer = (ModelImporter)assetImporter;
        importer.importAnimation = false;
        importer.importCameras = false;
        importer.importLights = false;
        importer.importBlendShapes = false;
        importer.preserveHierarchy = true;
        importer.isReadable = true; // The existing RuinCourtyard builds collision from the ground vertices.
        importer.addCollider = false;
        importer.meshCompression = ModelImporterMeshCompression.Off;
        importer.importNormals = ModelImporterNormals.Import;
        importer.importTangents = ModelImporterTangents.CalculateMikk;
    }

    private void OnPostprocessModel(GameObject model)
    {
        if (!assetPath.StartsWith(Root, StringComparison.Ordinal)) return;
        foreach(Renderer renderer in model.GetComponentsInChildren<Renderer>(true))
            if(renderer.name.Contains("rock stratum")) renderer.enabled=false;
        foreach (Transform node in model.GetComponentsInChildren<Transform>(true))
        {
            if (!node.name.StartsWith("RuinChunk_", StringComparison.Ordinal) ||
                node.name.EndsWith("_Near", StringComparison.Ordinal) ||
                node.name.EndsWith("_Far", StringComparison.Ordinal)) continue;
            Renderer[] near = node.GetComponentsInChildren<Renderer>(true)
                .Where(r => r.name.EndsWith("_Near", StringComparison.Ordinal)).ToArray();
            Renderer[] far = node.GetComponentsInChildren<Renderer>(true)
                .Where(r => r.name.EndsWith("_Far", StringComparison.Ordinal)).ToArray();
            if (near.Length == 0 || far.Length == 0) continue;
            foreach (Renderer renderer in near.Concat(far))
            {
                renderer.enabled = true;
                renderer.gameObject.SetActive(true);
            }
            LODGroup lod = node.GetComponent<LODGroup>();
            if(lod == null) lod = node.gameObject.AddComponent<LODGroup>();
            // Keep distant architecture visible to the extraction camera; only switch mesh detail.
            lod.SetLODs(new[] { new LOD(.16f, near), new LOD(.0001f, far) });
            lod.fadeMode = LODFadeMode.None;
            lod.RecalculateBounds();
        }
    }

    private void OnPreprocessTexture()
    {
        if (assetPath == Root + "RuinGround.png")
        {
            ((TextureImporter)assetImporter).maxTextureSize=512;
            return; // Compatibility resource loaded by V07; visible grass is now in the combined atlas.
        }
        if (assetPath != Root + "RuinAtlas.png") return;
        var importer = (TextureImporter)assetImporter;
        importer.textureType = TextureImporterType.Default;
        importer.sRGBTexture = true;
        importer.mipmapEnabled = false;
        importer.filterMode = FilterMode.Bilinear;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.maxTextureSize = 4096;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.alphaSource = TextureImporterAlphaSource.None;
    }
}
