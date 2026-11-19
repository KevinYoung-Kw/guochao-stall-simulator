using UnityEngine;
using UnityEngine.UI;

namespace Guochao
{
    public static class StallUIArt
    {
        public static Sprite RoundedPaper(out Texture2D texture)
        {
            texture=new Texture2D(64,64,TextureFormat.RGBA32,false){name="Rounded enamel paper",filterMode=FilterMode.Bilinear,wrapMode=TextureWrapMode.Clamp};
            Color32[] pixels=new Color32[4096];
            for(int y=0;y<64;y++)for(int x=0;x<64;x++)
            {
                Vector2 q=new Vector2(Mathf.Abs(x-31.5f)-17.5f,Mathf.Abs(y-31.5f)-17.5f);
                float d=new Vector2(Mathf.Max(q.x,0),Mathf.Max(q.y,0)).magnitude+Mathf.Min(Mathf.Max(q.x,q.y),0)-14;
                pixels[y*64+x]=new Color(1,1,1,Mathf.Clamp01(.5f-d));
            }
            texture.SetPixels32(pixels);texture.Apply(false,true);
            return Sprite.Create(texture,new Rect(0,0,64,64),new Vector2(.5f,.5f),100,0,SpriteMeshType.FullRect,new Vector4(19,19,19,19));
        }
    }
    // Original small vector illustrations: architectural silhouettes and dishes,
    // not borrowed game logos or decorative numbers without a mechanic.
    public sealed class StallUIIcon : MaskableGraphic
    {
        public bool district;
        public int index;
        private static readonly Color Ink=CampaignUI.Ink,Gold=CampaignUI.Gold,Red=CampaignUI.Vermilion,Cream=CampaignUI.PaperColor,Accent=CampaignUI.Muted;
        private Vector3 P(float x,float y){Rect r=rectTransform.rect;return new Vector3(r.center.x+x*r.width,r.center.y+y*r.height,0);}
        private void Tri(VertexHelper h,Vector2 a,Vector2 b,Vector2 c,Color colour)
        {int n=h.currentVertCount;h.AddVert(P(a.x,a.y),colour,Vector2.zero);h.AddVert(P(b.x,b.y),colour,Vector2.zero);h.AddVert(P(c.x,c.y),colour,Vector2.zero);h.AddTriangle(n,n+1,n+2);}
        private void Box(VertexHelper h,float x,float y,float w,float d,Color colour)
        {Tri(h,new Vector2(x-w/2,y-d/2),new Vector2(x+w/2,y-d/2),new Vector2(x+w/2,y+d/2),colour);Tri(h,new Vector2(x-w/2,y-d/2),new Vector2(x+w/2,y+d/2),new Vector2(x-w/2,y+d/2),colour);}
        private void Disc(VertexHelper h,float x,float y,float r,Color colour)
        {for(int i=0;i<24;i++){float a=i*Mathf.PI/12,b=(i+1)*Mathf.PI/12;Tri(h,new Vector2(x,y),new Vector2(x+Mathf.Cos(a)*r,y+Mathf.Sin(a)*r),new Vector2(x+Mathf.Cos(b)*r,y+Mathf.Sin(b)*r),colour);}}
        protected override void OnPopulateMesh(VertexHelper h)
        {
            h.Clear();
            if(district)
            {
                if(index==1){Disc(h,0,.06f,.32f,Ink);Disc(h,0,.08f,.27f,Gold);Box(h,0,-.13f,.78f,.12f,Ink);Box(h,0,.16f,.07f,.32f,Ink);return;}
                if(index==4){Box(h,0,.34f,.05f,.22f,Ink);Disc(h,0,0,.31f,Red);Box(h,0,.28f,.32f,.07f,Gold);Box(h,0,-.29f,.32f,.07f,Gold);Box(h,0,-.4f,.035f,.16f,Ink);Box(h,0,0,.05f,.42f,Gold);return;}
                if(index==5){Box(h,-.28f,-.11f,.10f,.60f,Ink);Box(h,.28f,-.11f,.10f,.60f,Ink);Box(h,0,.18f,.76f,.11f,Red);Box(h,0,.32f,.60f,.10f,Ink);Box(h,0,.03f,.62f,.05f,Gold);return;}
                Box(h,0,-.09f,.58f,.58f,Ink);Box(h,0,-.08f,.47f,.45f,Gold);
                if(index!=3)Tri(h,new Vector2(-.39f,.19f),new Vector2(.39f,.19f),new Vector2(0,.46f),Red);
                else{Box(h,0,.26f,.58f,.26f,Ink);Box(h,0,.30f,.47f,.12f,Gold);}
                for(int i=0;i<4;i++)Box(h,-.12f+(i%2)*.24f,.05f-(i/2)*.20f,.11f,.11f,Cream);
                if(index==2){Box(h,0,.42f,.03f,.25f,Ink);Tri(h,new Vector2(0,.48f),new Vector2(.22f,.42f),new Vector2(0,.35f),Red);}
                return;
            }
            if(index==2){Disc(h,0,0,.43f,Ink);Disc(h,0,0,.38f,Gold);for(int i=0;i<7;i++){float a=i*2.4f;Disc(h,Mathf.Sin(a)*.23f,Mathf.Cos(a)*.23f,.035f,i%2==0?Accent:Red);}return;}
            if(index==4){Tri(h,new Vector2(-.29f,-.45f),new Vector2(-.24f,-.45f),new Vector2(.34f,.43f),Ink);for(int i=0;i<3;i++){Disc(h,-.13f+i*.15f,-.18f+i*.22f,.13f,Ink);Disc(h,-.13f+i*.15f,-.18f+i*.22f,.10f,Red);}return;}
            if(index==5){Box(h,.10f,.32f,.055f,.30f,Ink);Tri(h,new Vector2(-.32f,.20f),new Vector2(.32f,.20f),new Vector2(.22f,-.38f),Ink);Tri(h,new Vector2(-.32f,.20f),new Vector2(.22f,-.38f),new Vector2(-.22f,-.38f),Ink);Box(h,0,-.05f,.40f,.40f,Red);Box(h,0,.20f,.70f,.07f,Gold);return;}
            Disc(h,0,-.04f,.40f,Ink);Box(h,0,.20f,.85f,.40f,Cream);Box(h,0,.02f,.86f,.07f,Ink);Box(h,0,-.36f,.32f,.07f,Ink);
            if(index==3){for(int i=0;i<3;i++)Box(h,-.18f+i*.18f,.28f,.045f,.19f,Gold);}
            else for(int i=0;i<7;i++){float a=i*2.4f;Disc(h,Mathf.Sin(a)*.26f,.11f+Mathf.Abs(Mathf.Cos(a))*.13f,.065f,i%3==0?Accent:Gold);}
        }
    }
}
