using System;
using UnityEditor;
using UnityEngine;

namespace Guochao.EditorTools
{
    // Import rules dedicated to the creation-screen (CreationV16) textures:
    // crisp NPOT UI art with real alpha. Only files under
    // Resources/UI/CreationV16 are touched; every other texture and font in
    // the project keeps its existing import settings.
    internal sealed class CreationUIAssetImporter : AssetPostprocessor
    {
        private void OnPreprocessTexture()
        {
            string path = assetPath.Replace('\\', '/');
            if (!path.Contains("Resources/UI/CreationV16/")) return;
            var texture = (TextureImporter)assetImporter;
            texture.npotScale = TextureImporterNPOTScale.None;
            texture.mipmapEnabled = false;
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.alphaIsTransparency = true;
            texture.maxTextureSize = 2048;

            // Only the runtime-sliced material atlas needs CPU readback and lossless
            // storage. Icons and the backdrop stay ordinary compressed UI textures.
            bool materialAtlas = path.EndsWith("creation-material-atlas.png", StringComparison.OrdinalIgnoreCase);
            texture.isReadable = materialAtlas;
            texture.textureCompression = materialAtlas
                ? TextureImporterCompression.Uncompressed
                : TextureImporterCompression.Compressed;
        }
    }
}
