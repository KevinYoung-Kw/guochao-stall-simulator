using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Guochao.Editor
{
    // Import/skin acceptance, independent of artwork previews and navigation tests.
    public static class AssetsV15Validation
    {
        [Serializable] public sealed class CharacterResult
        {
            public string id,role;
            public int triangles,bones,frames;
            public float lowestPoint=float.PositiveInfinity,highestPoint=float.NegativeInfinity;
            public float bakedUncompensatedLow=float.PositiveInfinity,bakedUncompensatedHigh=float.NegativeInfinity;
            public float bakedCompensatedLow=float.PositiveInfinity,bakedCompensatedHigh=float.NegativeInfinity;
            public string[] clips;
        }
        [Serializable] public sealed class Report
        {
            public bool success;
            public string scope="Actual Unity imported FBX/Legacy clips and deformed mesh samples; not gameplay or visual approval",failure="";
            public int customers,staff,vendors,shopTriangles;
            public List<CharacterResult> characters=new List<CharacterResult>();
        }
        static void Need(bool value,string message){if(!value)throw new InvalidOperationException(message);}
        public static void Run()
        {
            string[] args=Environment.GetCommandLineArgs();int at=Array.IndexOf(args,"--assets-v15-output");
            if(at<0||at+1>=args.Length)throw new ArgumentException("Specify --assets-v15-output");
            var report=new Report();
            try
            {
                foreach(var definition in CharacterAssetCatalog.All)
                {if(definition.IsCustomerSource)report.customers++;else if(definition.Role==CharacterRole.MarketStaff)report.staff++;else if(definition.IsPlayerSource)report.vendors++;}
                Need(report.customers==5&&report.staff==1&&report.vendors==1,"Catalog role counts incorrect.");
                foreach(string id in new[]{"customer_student_v15","market_cashier_v15"})
                {
                    Need(CharacterAssetCatalog.TryLoad(id,out var loaded,out string reason,CharacterMotion.Idle,CharacterMotion.Walk),reason);
                    Need(Resources.Load<Texture2D>(loaded.Definition.AlbedoResourcePath)!=null,"Missing original texture: "+id);
                    var root=UnityEngine.Object.Instantiate(loaded.Prefab);
                    try
                    {
                        var row=new CharacterResult{id=id,role=loaded.Definition.Role.ToString()};report.characters.Add(row);
                        var skins=root.GetComponentsInChildren<SkinnedMeshRenderer>();
                        Need(skins.Length>0,"Missing actual skinned geometry: "+id);
                        var bones=new HashSet<Transform>();
                        foreach(var skin in skins){row.triangles+=skin.sharedMesh.triangles.Length/3;foreach(var bone in skin.bones)bones.Add(bone);}
                        row.bones=bones.Count;Need(row.triangles==30000&&row.bones==52,"Unexpected runtime topology: "+id);
                        var names=new List<string>();
                        foreach(var pair in loaded.Clips)
                        {
                            var clip=pair.Value;names.Add(clip.name);Need(clip.legacy&&clip.length>1,"Empty or non-Legacy action: "+id);
                            for(int i=0;i<=8;i++)
                            {
                                clip.SampleAnimation(root,clip.length*i/8f);row.frames++;
                                foreach(var skin in skins)
                                {
                                    foreach(bool compensate in new[]{false,true})
                                    {
                                        var baked=new Mesh();skin.BakeMesh(baked,compensate);
                                        foreach(var vertex in baked.vertices)
                                        {
                                            float y=skin.transform.TransformPoint(vertex).y;
                                            if(compensate){row.bakedCompensatedLow=Mathf.Min(row.bakedCompensatedLow,y);row.bakedCompensatedHigh=Mathf.Max(row.bakedCompensatedHigh,y);}
                                            else{row.bakedUncompensatedLow=Mathf.Min(row.bakedUncompensatedLow,y);row.bakedUncompensatedHigh=Mathf.Max(row.bakedUncompensatedHigh,y);}
                                        }
                                        UnityEngine.Object.DestroyImmediate(baked);
                                    }
                                    // Independently evaluate the imported bind-pose matrices
                                    // and every weight. This avoids assuming BakeMesh's scale
                                    // compensation contract while measuring the rendered skin.
                                    Mesh source=skin.sharedMesh;var vertices=source.vertices;var bind=source.bindposes;
                                    var matrices=new Matrix4x4[skin.bones.Length];
                                    for(int b=0;b<matrices.Length;b++)matrices[b]=skin.bones[b].localToWorldMatrix*bind[b];
                                    var counts=source.GetBonesPerVertex();var weights=source.GetAllBoneWeights();int cursor=0;
                                    for(int v=0;v<vertices.Length;v++)
                                    {
                                        Vector3 p=Vector3.zero;
                                        for(int w=0;w<counts[v];w++){var weight=weights[cursor++];p+=matrices[weight.boneIndex].MultiplyPoint3x4(vertices[v])*weight.weight;}
                                        Need(!float.IsNaN(p.x)&&!float.IsInfinity(p.x)&&!float.IsNaN(p.y)&&!float.IsInfinity(p.y)&&!float.IsNaN(p.z)&&!float.IsInfinity(p.z),"Nonfinite skin: "+id);
                                        row.lowestPoint=Mathf.Min(row.lowestPoint,p.y);row.highestPoint=Mathf.Max(row.highestPoint,p.y);
                                    }
                                    counts.Dispose();weights.Dispose();
                                }
                            }
                        }
                        row.clips=names.ToArray();Need(row.highestPoint>1.3f&&row.highestPoint<2f&&row.lowestPoint>-.15f,"Unexpected imported scale or deformation: "+id);
                    }
                    finally{UnityEngine.Object.DestroyImmediate(root);}
                }
                var shop=Resources.Load<GameObject>("Models/StreetShopfrontAI");Need(shop!=null,"Real street shop model missing.");
                foreach(var mesh in shop.GetComponentsInChildren<MeshFilter>(true))report.shopTriangles+=mesh.sharedMesh.triangles.Length/3;
                Need(report.shopTriangles==30000,"Unexpected shop runtime geometry.");
                Need(Resources.Load<Texture2D>("Models/Textures/StreetShopfrontAI_Albedo")!=null,"Shop original texture missing.");
                report.success=true;
            }
            catch(Exception error){report.failure=error.ToString();}
            string path=Path.GetFullPath(args[at+1]);Directory.CreateDirectory(Path.GetDirectoryName(path));File.WriteAllText(path,JsonUtility.ToJson(report,true));
            Debug.Log("ASSETS_V15 "+report.success+" "+report.failure);EditorApplication.Exit(report.success?0:1);
        }
    }
}
