using System.IO;
using UnityEditor;

namespace Guochao.Editor
{
    // Customer clips use the same deterministic Legacy import as the vendor.
    public sealed class CustomerAssetImporter : AssetPostprocessor
    {
        void OnPreprocessModel()
        {
            if(!assetPath.StartsWith("Assets/Resources/Models/Customer") &&
               !assetPath.StartsWith("Assets/Resources/Models/MarketCashier"))return;
            var importer=(ModelImporter)assetImporter;
            importer.importAnimation=true;importer.animationType=ModelImporterAnimationType.Legacy;
            importer.importCameras=false;importer.importLights=false;importer.globalScale=1;
            importer.useFileScale=true;importer.isReadable=true;
        }
    }
}
