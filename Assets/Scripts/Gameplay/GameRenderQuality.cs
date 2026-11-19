using UnityEngine;

namespace Guochao
{
    public static class GameRenderQuality
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Configure()
        {
            // Web used to select High (no MSAA), unlike the native build.
            QualitySettings.SetQualityLevel(QualitySettings.names.Length - 1, true);
            QualitySettings.antiAliasing = 4;
            QualitySettings.globalTextureMipmapLimit = 0;
            QualitySettings.anisotropicFiltering = AnisotropicFiltering.ForceEnable;
            QualitySettings.pixelLightCount = 4;
            QualitySettings.shadowResolution = ShadowResolution.High;
            Application.targetFrameRate = 60;
            Application.runInBackground = true;
            Debug.Log("RENDER_QUALITY full-resolution textures; MSAA=4; anisotropic=forced; targetFPS=60");
        }
    }
}
