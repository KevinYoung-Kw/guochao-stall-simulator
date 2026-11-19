using UnityEngine;

namespace Guochao
{
    // Functional equipment geometry for the loadout. AI hero furniture is installed separately.
    public static class EquipmentVisualFactory
    {
        static readonly Color Metal=new Color(.62f,.64f,.67f),Dark=new Color(.14f,.22f,.30f),Cream=new Color(.86f,.81f,.65f),Red=new Color(.64f,.22f,.12f);
        public static bool IsHeavy(string id) => id.StartsWith("B") || id=="E03"||id=="E05"||id=="E08"||id=="V15"||id=="V08";
        public static GameObject Ingredient(string id,Transform parent)
        {
            id=SupplyCatalog.IngredientId(id);
            var root=new GameObject("Ingredient_"+id);root.transform.SetParent(parent,false);
            Box(root,"CrateBase",new Vector3(0,.025f,0),new Vector3(.40f,.05f,.36f),Cream);
            for(int i=0;i<2;i++)Box(root,"CrateRim",new Vector3((i==0?-1:1)*.19f,.1f,0),new Vector3(.025f,.15f,.36f),Cream);
            if(id=="prepared")
            {
                GameArtRendering.Apply(root,true);
                if(GeneratedIngredientAssets.TryPopulatePrepared(root.transform))return root;
            }
            if(id=="seasoning")
            {Disc(root,"OilSaltBucket",new Vector3(0,.16f,0),.14f,.24f,new Color(.55f,.30f,.16f));Disc(root,"Lid",new Vector3(0,.29f,0),.15f,.025f,Cream);}
            else for(int i=0;i<6;i++)
            {
                Vector3 at=new Vector3(-.11f+i%2*.22f,.13f,-.11f+i/2*.10f);
                if(id=="fruit")Sphere(root,"FreshLemon",at,new Vector3(.075f,.06f,.05f),new Color(.88f,.75f,.18f));
                else if(id=="skewer")Disc(root,"Sausage",at,.027f,.17f,Red);
                else if(id=="dough")Disc(root,"Dough",at,.08f,.035f,Cream);
                else Box(root,"SealedMeal",at,new Vector3(.15f,.035f,.08f),new Color(.23f,.32f,.44f));
            }
            GameArtRendering.Apply(root,true);return root;
        }
        public static GameObject Create(string id,Transform parent)
        {
            var root=new GameObject("Equipment_"+id);root.transform.SetParent(parent,false);
            if(GeneratedEquipmentAssets.TryPopulate(id,root.transform))return root;
            if(id=="B04")
            {
                var source=Resources.Load<GameObject>("Models/Stove");
                if(source){Object.Instantiate(source,root.transform,false);GameArtRendering.Apply(root,true);return root;}
            }
            if(id=="B07"||id=="B08")
            {
                Box(root,"EnamelCabinet",new Vector3(0,.24f,0),new Vector3(.62f,.47f,.44f),Cream);
                Box(root,"DoorFrame",new Vector3(-.065f,.25f,.23f),new Vector3(.43f,.32f,.035f),Dark);
                Box(root,"TintedWindow",new Vector3(-.065f,.25f,.252f),new Vector3(.34f,.23f,.012f),new Color(.22f,.29f,.36f));
                Box(root,"DoorHandle",new Vector3(.16f,.25f,.28f),new Vector3(.035f,.23f,.05f),Metal);
                for(int i=0;i<2;i++)Disc(root,"Dial",new Vector3(.245f,.17f+i*.16f,.245f),.035f,.025f,Metal,true);
            }
            else if(id=="RiceBucket")
            {
                Disc(root,"InsulatedRiceBucket",new Vector3(0,.21f,0),.22f,.40f,Cream);
                Disc(root,"Lid",new Vector3(0,.425f,0),.23f,.04f,Metal);
                Box(root,"LidHandle",new Vector3(0,.47f,0),new Vector3(.18f,.045f,.055f),Dark);
            }
            else if(id=="E01")
            {
                Box(root,"CoalBox",new Vector3(0,.12f,0),new Vector3(.32f,.23f,.30f),Cream);
                for(int i=0;i<4;i++)Disc(root,"Coal",new Vector3(i%2*.13f-.065f,.24f,i/2*.13f-.065f),.055f,.045f,Dark);
            }
            else if(id=="E10")
            {
                Box(root,"LockBar",new Vector3(0,.025f,0),new Vector3(.18f,.05f,.05f),Dark);
                for(int i=0;i<2;i++)Box(root,"LockArm",new Vector3(i==0?-.07f:.07f,.12f,0),new Vector3(.025f,.20f,.025f),Metal);
                Box(root,"LockBow",new Vector3(0,.22f,0),new Vector3(.16f,.025f,.025f),Metal);
            }
            else if(id=="E15")
            {
                Disc(root,"FoldedUmbrellaPole",new Vector3(0,.57f,0),.018f,1.14f,Metal);
                Disc(root,"FoldedCanopy",new Vector3(0,.78f,0),.075f,.64f,Dark);
            }
            else if(id.StartsWith("S"))
            {
                Disc(root,"CondimentJar",new Vector3(0,.09f,0),.055f,.17f,new Color(.55f,.27f,.13f));
                Disc(root,"ScrewCap",new Vector3(0,.185f,0),.058f,.025f,Cream);
            }
            else if(id=="E03")
            {
                Disc(root,"GasTank",new Vector3(0,.3f,0),.19f,.50f,new Color(.55f,.48f,.34f));
                Sphere(root,"Shoulder",new Vector3(0,.54f,0),new Vector3(.19f,.095f,.19f),Metal);
                Disc(root,"Valve",new Vector3(0,.65f,0),.035f,.09f,Red);
                Box(root,"GuardHandle",new Vector3(0,.7f,0),new Vector3(.24f,.06f,.08f),Metal);
            }
            else if(id=="E05"||id=="E08")
            {
                Box(root,"Body",new Vector3(0,.20f,0),new Vector3(.52f,.38f,.36f),id=="E05"?Dark:new Color(.27f,.36f,.48f));
                Box(root,"Lid",new Vector3(0,.41f,0),new Vector3(.56f,.06f,.40f),Cream);
                for(int i=0;i<2;i++)Box(root,id=="E05"?"Terminal":"Latch",new Vector3(i==0?-.16f:.16f,id=="E05"?.46f:.32f,id=="E05"?0:.205f),new Vector3(.07f,.06f,.06f),i==0?Red:Metal);
                Box(root,"CarryHandle",new Vector3(0,.48f,0),new Vector3(.26f,.04f,.055f),Dark);
            }
            else if(id=="V15")
            {
                Box(root,"IceCounter",new Vector3(0,.17f,0),new Vector3(.70f,.32f,.45f),Metal);
                Box(root,"IceWell",new Vector3(-.15f,.34f,0),new Vector3(.32f,.02f,.32f),new Color(.58f,.82f,.82f));
                for(int i=0;i<6;i++)Box(root,"Ice",new Vector3(-.24f+i%3*.085f,.365f,-.08f+i/3*.12f),Vector3.one*.06f,Color.white);
                Box(root,"CutBoard",new Vector3(.20f,.35f,0),new Vector3(.22f,.025f,.32f),Cream);
            }
            else if(id=="B01")
            {
                Disc(root,"ClayBrazier",new Vector3(0,.23f,0),.23f,.42f,new Color(.58f,.30f,.17f));
                Disc(root,"CoalOpening",new Vector3(0,.45f,0),.18f,.025f,Dark);
                Box(root,"AirDoor",new Vector3(0,.12f,.232f),new Vector3(.13f,.1f,.02f),Dark);
            }
            else if(id.StartsWith("B"))
            {
                bool cassette=id=="B02";
                Box(root,"StoveBody",new Vector3(0,.15f,0),new Vector3(cassette?.47f:.60f,.25f,.43f),cassette?new Color(.57f,.24f,.16f):Metal);
                Disc(root,"Burner",new Vector3(cassette?-.06f:0,.295f,0),.15f,.035f,Dark);
                for(int i=0;i<4;i++){var g=Box(root,"PotSupport",new Vector3(0,.33f,0),new Vector3(.37f,.025f,.025f),Metal);g.transform.localRotation=Quaternion.Euler(0,i*45,0);}
                Disc(root,"Control",new Vector3(.20f,.16f,.225f),.035f,.025f,Dark,true);
            }
            else if(id=="V08")
            {
                Box(root,"PotRack",new Vector3(0,.07f,0),new Vector3(.70f,.07f,.28f),Metal);
                for(int i=0;i<3;i++){Disc(root,"ClayPot",new Vector3((i-1)*.23f,.18f,0),.10f,.18f,new Color(.58f,.34f,.18f));Sphere(root,"Lid",new Vector3((i-1)*.23f,.29f,0),new Vector3(.105f,.035f,.105f),Cream);}
            }
            else if(id.StartsWith("V"))
            {
                Disc(root,"Vessel",new Vector3(0,.12f,0),.24f,id=="V09"?.40f:.15f,Metal);
                Disc(root,"InnerWell",new Vector3(0,id=="V09"?.33f:.205f,0),.21f,.012f,Dark);
                Box(root,"Handle",new Vector3(.32f,.16f,0),new Vector3(.30f,.045f,.06f),Dark);
            }
            else if(id.StartsWith("P"))
            {
                Packing(id,root);
            }
            else
            {
                Box(root,"ToolHandle",new Vector3(0,.18f,0),new Vector3(.055f,.34f,.055f),Cream);
                Sphere(root,"ToolHead",new Vector3(0,.39f,0),new Vector3(.11f,.08f,.035f),Metal);
            }
            GameArtRendering.Apply(root,true);return root;
        }
        // 包材图标：每种 P 包材有独立轮廓，160px 下图与图之间一眼可辨。
        // 全部用 Unity primitive 简单建模，不称 AI 生成。
        static void Packing(string id,GameObject root)
        {
            Color kraft=new Color(.55f,.40f,.24f),foam=new Color(.96f,.96f,.90f),paleLid=new Color(.78f,.72f,.56f),tub=new Color(.90f,.90f,.84f);
            if(id=="P04") // 扁身牛皮防油纸袋，区别于厚方形餐盒
            {
                Box(root,"KraftPaperBag",new Vector3(0,.14f,0),new Vector3(.22f,.27f,.055f),kraft);
                Box(root,"FoldedMouth",new Vector3(0,.275f,0),new Vector3(.225f,.025f,.06f),new Color(.47f,.33f,.19f));
                Box(root,"PaperFold",new Vector3(0,.06f,-.029f),new Vector3(.19f,.012f,.006f),new Color(.70f,.54f,.35f));
                Disc(root,"RedStamp",new Vector3(0,.16f,-.031f),.035f,.006f,Red,true);
            }
            else if(id=="P05") // 方形 PP 卡扣密封餐盒：扁矩形盒身+盖+两侧红卡扣
            {
                Box(root,"BoxBase",new Vector3(0,.05f,0),new Vector3(.26f,.10f,.20f),Dark);
                Box(root,"ClearLid",new Vector3(0,.108f,0),new Vector3(.28f,.018f,.22f),Cream);
                Box(root,"SealClipL",new Vector3(-.145f,.09f,0),new Vector3(.02f,.05f,.07f),Red);
                Box(root,"SealClipR",new Vector3(.145f,.09f,0),new Vector3(.02f,.05f,.07f),Red);
            }
            else if(id=="P08") // 封口饮料杯：竖直圆杯+封口膜+吸管，绝不画成饭盒
            {
                Disc(root,"CupBody",new Vector3(0,.085f,0),.075f,.17f,new Color(.82f,.91f,.93f));
                Disc(root,"FilmSeal",new Vector3(0,.176f,0),.085f,.014f,Color.white);
                Disc(root,"Straw",new Vector3(.028f,.225f,0),.009f,.11f,Red);
            }
            else if(id=="P07") // 圆纸汤碗：矮圆碗+翻边+碗盖
            {
                Disc(root,"BowlBody",new Vector3(0,.055f,0),.125f,.11f,Cream);
                Disc(root,"BowlRim",new Vector3(0,.112f,0),.135f,.018f,paleLid);
                Disc(root,"BowlLid",new Vector3(0,.128f,0),.125f,.016f,Cream);
                Disc(root,"LidKnob",new Vector3(0,.142f,0),.02f,.012f,paleLid);
            }
            else if(id=="P06") // 分格大餐盒：最大 footprint 方盒+盖上分格条
            {
                Box(root,"BoxBase",new Vector3(0,.055f,0),new Vector3(.32f,.11f,.26f),Cream);
                Box(root,"BoxLid",new Vector3(0,.118f,0),new Vector3(.34f,.018f,.28f),paleLid);
                Box(root,"DividerLong",new Vector3(0,.128f,0),new Vector3(.012f,.012f,.24f),paleLid);
                Box(root,"DividerCross",new Vector3(0,.128f,0),new Vector3(.30f,.012f,.012f),paleLid);
            }
            else if(id=="P02") // 发泡盒：白色泡沫箱+外凸盖沿
            {
                Box(root,"FoamBase",new Vector3(0,.055f,0),new Vector3(.28f,.11f,.23f),foam);
                Box(root,"FoamLidRim",new Vector3(0,.105f,0),new Vector3(.31f,.02f,.26f),foam);
                Box(root,"FoamLid",new Vector3(0,.125f,0),new Vector3(.30f,.035f,.25f),Color.white);
            }
            else if(id=="P03") // 防油纸托和竹签
            {
                Box(root,"PaperTray",new Vector3(0,.035f,0),new Vector3(.25f,.04f,.18f),new Color(.80f,.70f,.52f));
                for(int i=0;i<3;i++)Box(root,"BambooSkewer",new Vector3(-.07f+i*.07f,.075f,0),new Vector3(.012f,.012f,.23f),new Color(.69f,.49f,.24f));
            }
            else if(id=="P09") // 高身提手纸袋
            {
                Box(root,"CarrierBag",new Vector3(0,.11f,0),new Vector3(.19f,.22f,.14f),new Color(.88f,.83f,.66f));
                Box(root,"CarrierRim",new Vector3(0,.225f,0),new Vector3(.20f,.025f,.15f),Cream);
                Box(root,"CarrierHandle",new Vector3(0,.27f,0),new Vector3(.11f,.012f,.012f),Dark);
                Box(root,"RedSleeve",new Vector3(0,.11f,-.074f),new Vector3(.20f,.09f,.012f),Red);
            }
            else if(id=="P10") // 青花纹纸瓦罐
            {
                Disc(root,"PorcelainJar",new Vector3(0,.09f,0),.10f,.18f,foam);
                Disc(root,"BlueBand",new Vector3(0,.09f,0),.102f,.035f,new Color(.20f,.34f,.55f));
                Disc(root,"JarLid",new Vector3(0,.19f,0),.105f,.025f,foam);
            }
            else if(id=="P11") // 竹编食盒
            {
                Color bamboo=new Color(.72f,.56f,.34f);
                Box(root,"BambooBox",new Vector3(0,.055f,0),new Vector3(.28f,.11f,.21f),bamboo);
                Box(root,"BambooLid",new Vector3(0,.12f,0),new Vector3(.30f,.02f,.23f),new Color(.87f,.74f,.51f));
                for(int i=-1;i<=1;i++)Box(root,"WeaveBand",new Vector3(i*.07f,.132f,0),new Vector3(.012f,.008f,.21f),bamboo);
            }
            else if(id=="P12") // 竹筒杯
            {
                Color bamboo=new Color(.72f,.56f,.34f);
                Disc(root,"BambooCup",new Vector3(0,.10f,0),.075f,.20f,bamboo);
                Disc(root,"BambooRim",new Vector3(0,.205f,0),.082f,.014f,new Color(.46f,.34f,.20f));
                Disc(root,"BambooSeal",new Vector3(0,.214f,0),.071f,.008f,Cream);
            }
            else // 未知 P 包材保底：扁盒堆叠
            {
                for(int i=0;i<5;i++)Box(root,"PackingStack",new Vector3(0,.04f+i*.025f,0),new Vector3(.28f,.022f,.23f),Cream);
            }
        }
        static GameObject Shape(GameObject root,string name,PrimitiveType type,Vector3 p,Vector3 scale,Color colour)
        {
            var go=GameObject.CreatePrimitive(type);go.name=name;go.transform.SetParent(root.transform,false);go.transform.localPosition=p;go.transform.localScale=scale;
            var collider=go.GetComponent<Collider>();if(Application.isPlaying)Object.Destroy(collider);else Object.DestroyImmediate(collider);
            var m=new Material(Shader.Find("Standard"));m.color=colour;m.SetFloat("_Glossiness",.12f);go.GetComponent<Renderer>().sharedMaterial=m;return go;
        }
        static GameObject Box(GameObject r,string n,Vector3 p,Vector3 s,Color c)=>Shape(r,n,PrimitiveType.Cube,p,s,c);
        static void Sphere(GameObject r,string n,Vector3 p,Vector3 s,Color c)=>Shape(r,n,PrimitiveType.Sphere,p,s*2,c);
        static void Disc(GameObject r,string n,Vector3 p,float radius,float height,Color c,bool front=false)
        {var o=Shape(r,n,PrimitiveType.Cylinder,p,new Vector3(radius*2,height*.5f,radius*2),c);if(front)o.transform.localRotation=Quaternion.Euler(90,0,0);}
    }
}
