using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Guochao.Editor
{
    public sealed class GameAssetImporter : AssetPostprocessor
    {
        void OnPreprocessModel()
        {
            if (!assetPath.StartsWith("Assets/Resources/Models/")) return;
            var importer = (ModelImporter)assetImporter;
            importer.globalScale = 1;
            importer.useFileScale = true;
            importer.importCameras = false;
            importer.importLights = false;
            importer.isReadable = true;
            importer.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
            if (Path.GetFileName(assetPath).StartsWith("Player"))
            {
                importer.importAnimation = true;
                importer.animationType = ModelImporterAnimationType.Legacy;
            }
        }
        void OnPreprocessTexture()
        {
            var importer = (TextureImporter)assetImporter;
            if (assetPath.Contains("/UI/Skins/") || assetPath.Contains("/UI/Icons/"))
            {
                importer.textureType = TextureImporterType.Sprite;
                importer.alphaIsTransparency = true;
                importer.mipmapEnabled = false;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.maxTextureSize = 2048;
            }
        }
    }

    public static class GuochaoBuild
    {
        const string ScenePath = "Assets/Scenes/Playable.unity";
        static string Delivery
        {
            get
            {
                string[] args = Environment.GetCommandLineArgs();
                int index = Array.IndexOf(args, "--guochao-delivery");
                if (index >= 0)
                {
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]) || args[index + 1].StartsWith("--"))
                        throw new ArgumentException("--guochao-delivery requires an output directory.");
                    return Path.GetFullPath(args[index + 1]);
                }
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads/Codex/摆摊模拟器-基础美术/可玩原型-v13");
            }
        }

        [MenuItem("Guochao/Prepare playable scene")]
        public static void Prepare()
        {
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            Directory.CreateDirectory("Assets/Scenes");
            Directory.CreateDirectory("Assets/Materials");
            foreach (string path in Directory.GetFiles("Assets/Resources/Models", "*.fbx"))
            {
                var importer = AssetImporter.GetAtPath(path) as ModelImporter;
                if (importer == null) continue;
                if (Path.GetFileName(path).StartsWith("Player") && importer.animationType != ModelImporterAnimationType.Legacy)
                { importer.animationType = ModelImporterAnimationType.Legacy; importer.SaveAndReimport(); }
                if (Path.GetFileName(path).StartsWith("Player")) ApplyMaterial(path, "Player");
                if (Path.GetFileName(path) == "Stove.fbx") ApplyMaterial(path, "Stove");
                if (Path.GetFileName(path) == "CartAI.fbx") ApplyCartMaterial(path);
            }
            PlayerSettings.companyName = "Guochao Studio";
            PlayerSettings.productName = "摆摊模拟器·清晨出摊";
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Standalone, "com.guochao.streetstall");
            PlayerSettings.defaultScreenWidth = 1600;
            PlayerSettings.defaultScreenHeight = 900;
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.runInBackground = true;
            PlayerSettings.colorSpace = ColorSpace.Linear;
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Standalone, ScriptingImplementation.Mono2x);
            PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Disabled;
            PlayerSettings.WebGL.exceptionSupport = WebGLExceptionSupport.FullWithStacktrace;
            PlayerSettings.WebGL.template = "PROJECT:Guochao";
            QualitySettings.antiAliasing = 4;
            QualitySettings.shadows = ShadowQuality.All;
            QualitySettings.shadowResolution = ShadowResolution.High;
            QualitySettings.shadowDistance = 55;
            var settings = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset")[0]);
            var input = settings.FindProperty("activeInputHandler");
            if (input != null) input.intValue = 0;
            // Unity 6 Metal's legacy presentation path stalls on this Apple Silicon host.
            var displayLink = settings.FindProperty("metalUseMetalDisplayLink");
            if (displayLink != null) displayLink.boolValue = true;
            settings.ApplyModifiedPropertiesWithoutUndo();
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var root = new GameObject("Guochao_Playable");
            root.AddComponent<Guochao.GameFlow>();
            root.AddComponent<Guochao.ArtHUD>();
            root.AddComponent<Guochao.PlayableCapture>();
            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            AssetDatabase.SaveAssets();
            Debug.Log("GUOCHAO_PREPARE_OK");
        }

        static void ApplyMaterial(string path, string family)
        {
            string texturePath = "Assets/Resources/Models/Textures/" + family + "_Image_0.png";
            Texture2D albedo = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
            if (albedo == null) { Debug.LogWarning("Missing model albedo: " + texturePath); return; }
            string matPath = "Assets/Materials/" + family + ".mat";
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (mat == null) { mat = new Material(Shader.Find("Standard")); AssetDatabase.CreateAsset(mat, matPath); }
            mat.mainTexture = albedo;
            mat.color = Color.white;
            mat.SetFloat("_Glossiness", family == "Player" ? .2f : .32f);
            mat.SetFloat("_Metallic", family == "Player" ? 0f : .1f);
            var importer = (ModelImporter)AssetImporter.GetAtPath(path);
            foreach (Material embedded in AssetDatabase.LoadAllAssetsAtPath(path).OfType<Material>())
                importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), embedded.name), mat);
            importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), family + "_Material_00"), mat);
            importer.SaveAndReimport();
        }

        static void ApplyCartMaterial(string path)
        {
            const string textureFolder = "Assets/Resources/Models/Textures/";
            const string packedPath = textureFolder + "CartAI_MetallicSmoothness.png";
            string source = textureFolder + "CartAI_texture_01.png";
            if (!File.Exists(source)) throw new FileNotFoundException("CartAI PBR source missing", source);
            if (!File.Exists(packedPath) || File.GetLastWriteTimeUtc(packedPath) < File.GetLastWriteTimeUtc(source))
            {
                Texture2D data = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
                data.LoadImage(File.ReadAllBytes(source));
                Color[] pixels = data.GetPixels();
                for (int i = 0; i < pixels.Length; i++) pixels[i] = new Color(pixels[i].b * .1f, 0, 0, 1 - pixels[i].g);
                data.SetPixels(pixels); data.Apply();
                File.WriteAllBytes(packedPath, data.EncodeToPNG()); UnityEngine.Object.DestroyImmediate(data);
                AssetDatabase.ImportAsset(packedPath, ImportAssetOptions.ForceSynchronousImport);
            }
            var textureImporter = (TextureImporter)AssetImporter.GetAtPath(packedPath);
            if (textureImporter.sRGBTexture) { textureImporter.sRGBTexture = false; textureImporter.SaveAndReimport(); }
            const string materialPath = "Assets/Materials/CartAI.mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (material == null) { material = new Material(Shader.Find("Standard")); AssetDatabase.CreateAsset(material, materialPath); }
            material.color = Color.white;
            material.mainTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(textureFolder + "CartAI_texture_00.png");
            material.SetTexture("_MetallicGlossMap", AssetDatabase.LoadAssetAtPath<Texture2D>(packedPath));
            material.EnableKeyword("_METALLICGLOSSMAP"); material.SetFloat("_GlossMapScale", 1);
            var importer = (ModelImporter)AssetImporter.GetAtPath(path);
            foreach (Material embedded in AssetDatabase.LoadAllAssetsAtPath(path).OfType<Material>())
                if (embedded.name.StartsWith("Designer_Logic_PBR", StringComparison.Ordinal))
                    importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), embedded.name), material);
            importer.SaveAndReimport();
        }

        public static void BuildMac() { Prepare(); Build(BuildTarget.StandaloneOSX, Path.Combine(Delivery, "摆摊模拟器.app")); }
        public static void BuildWeb() { Prepare(); Build(BuildTarget.WebGL, Path.Combine(Delivery, "Web")); }
        static void Build(BuildTarget target, string destination)
        {
            Directory.CreateDirectory(Delivery);
            BuildReport report = BuildPipeline.BuildPlayer(new BuildPlayerOptions { scenes = new[] { ScenePath }, locationPathName = destination, target = target, options = BuildOptions.None });
            Debug.Log("GUOCHAO_BUILD " + report.summary.result + " " + report.summary.totalSize);
            if (report.summary.result != BuildResult.Succeeded) throw new Exception("Build failed: " + report.summary.result);
        }
    }
}
