using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Guochao.Editor
{
    // Desktop Web release: retain every source pixel and UI glyph. Crunch adds
    // on-disk compression to the existing DXT path; it does not shrink textures.
    public static class WebRelease20
    {
        [Serializable] sealed class Item { public string path; public int maximumSize; public int quality; }
        [Serializable] sealed class Record { public string method="WebGL-only model-texture Crunch, quality 85, original resolution; fonts/UI/audio unchanged"; public List<Item> textures=new List<Item>(); }
        public static void BuildWeb()
        {
            string[] args=Environment.GetCommandLineArgs();int at=Array.IndexOf(args,"--guochao-delivery");
            if(at<0||at+1>=args.Length)throw new ArgumentException("Explicit --guochao-delivery required");
            string delivery=Path.GetFullPath(args[at+1]);Directory.CreateDirectory(delivery);
            var old=new Dictionary<string,TextureImporterPlatformSettings>();var record=new Record();
            try
            {
                foreach(string path in Directory.GetFiles("Assets/Resources/Models/Textures","*.png",SearchOption.AllDirectories))
                {
                    var importer=AssetImporter.GetAtPath(path) as TextureImporter;if(importer==null)continue;
                    var prior=importer.GetPlatformTextureSettings("WebGL");old.Add(path,prior);
                    var settings=importer.GetPlatformTextureSettings("WebGL");
                    settings.name="WebGL";settings.overridden=true;settings.maxTextureSize=prior.overridden?prior.maxTextureSize:importer.maxTextureSize;
                    settings.format=TextureImporterFormat.Automatic;settings.textureCompression=TextureImporterCompression.Compressed;
                    settings.crunchedCompression=true;settings.compressionQuality=85;
                    importer.SetPlatformTextureSettings(settings);importer.SaveAndReimport();
                    record.textures.Add(new Item{path=path,maximumSize=settings.maxTextureSize,quality=85});
                }
                File.WriteAllText(Path.Combine(delivery,"texture-compression.json"),JsonUtility.ToJson(record,true));
                GuochaoBuild.BuildWeb();
            }
            finally
            {
                foreach(var pair in old)
                {
                    var importer=AssetImporter.GetAtPath(pair.Key) as TextureImporter;
                    if(importer==null)continue;
                    importer.SetPlatformTextureSettings(pair.Value);importer.SaveAndReimport();
                }
                AssetDatabase.SaveAssets();
            }
        }
    }
}
