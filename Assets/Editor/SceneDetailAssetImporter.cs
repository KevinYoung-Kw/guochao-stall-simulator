using UnityEditor;
namespace Guochao.Editor
{
    public sealed class SceneDetailAssetImporter : AssetPostprocessor
    {
        void OnPreprocessModel()
        {
            if(!assetPath.StartsWith("Assets/Resources/Models/HomeBedAI") && !assetPath.StartsWith("Assets/Resources/Models/MarketProduceAI") && !assetPath.StartsWith("Assets/Resources/Models/MarketCheckoutDetails"))return;
            var importer=(ModelImporter)assetImporter;importer.globalScale=1;importer.useFileScale=true;
            importer.importAnimation=false;importer.importCameras=false;importer.importLights=false;importer.isReadable=true;
        }
    }
}
