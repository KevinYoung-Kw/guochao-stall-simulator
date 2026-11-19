using UnityEngine;
using UnityEngine.UI;

namespace Guochao
{
    public enum SelectionSymbolKind
    {
        // Career identity objects: one recognisable prop per origin, no reuse.
        CareerWhistle, CareerWok, CareerKeyboard, CareerChefHat, CareerHelmet,
        CareerBoba, CareerLadle, CareerLedger,
        // Trait grouping marks.
        TraitUp, TraitDown, TraitHeart,
        // Semantic trait objects: learning/reading, carrying, luck, sight,
        // hearing, night owl.
        TraitBook, TraitBag, TraitDice, TraitEye, TraitEar, TraitMoon,
        // Hobby items.
        HobbySkewer, HobbyBoba, HobbyCandy, HobbyDumbbell, HobbyNoodles, HobbySoup,
        // Ledger / header meta marks.
        MetaCoin, MetaStar, MetaWrench, MetaGift, MarkTick, MarkAlert,
        // Real starting-gear semantics for the creation ledger: U14 料理剪 and
        // U15 抹布 must never fall back to the generic ladle/spoon photograph.
        GearScissors, GearCloth
    }

    // Original flat vector pictograms for the pre-run panels, drawn in the
    // shared CampaignUI paper/ink/vermilion/gold vocabulary. Primary strokes
    // use the graphic's own colour so callers can mute disabled rows; small
    // accents are fixed palette constants. Pure OnPopulateMesh geometry: no
    // textures, no per-frame allocation, raycast disabled by default.
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class SelectionSymbol : MaskableGraphic
    {
        public SelectionSymbolKind kind;
        private static readonly Color Gold=CampaignUI.Gold,Red=CampaignUI.Vermilion,Cream=CampaignUI.PaperColor,Muted=CampaignUI.Muted;

        public static SelectionSymbol Create(Transform parent,string name,SelectionSymbolKind kind,Vector2 size)
        {
            var go=new GameObject(name,typeof(RectTransform));go.transform.SetParent(parent,false);
            var rect=go.GetComponent<RectTransform>();rect.sizeDelta=size;
            go.AddComponent<CanvasRenderer>();
            var symbol=go.AddComponent<SelectionSymbol>();symbol.kind=kind;symbol.color=CampaignUI.Ink;symbol.raycastTarget=false;
            return symbol;
        }
        public void SetKind(SelectionSymbolKind value){if(kind==value)return;kind=value;SetVerticesDirty();}

        private Vector3 P(float x,float y){Rect r=rectTransform.rect;return new Vector3(r.center.x+x*r.width,r.center.y+y*r.height,0);}
        private void Tri(VertexHelper h,Vector2 a,Vector2 b,Vector2 c,Color colour)
        {int n=h.currentVertCount;h.AddVert(P(a.x,a.y),colour,Vector2.zero);h.AddVert(P(b.x,b.y),colour,Vector2.zero);h.AddVert(P(c.x,c.y),colour,Vector2.zero);h.AddTriangle(n,n+1,n+2);}
        private void Box(VertexHelper h,float x,float y,float w,float d,Color colour)
        {Tri(h,new Vector2(x-w/2,y-d/2),new Vector2(x+w/2,y-d/2),new Vector2(x+w/2,y+d/2),colour);Tri(h,new Vector2(x-w/2,y-d/2),new Vector2(x+w/2,y+d/2),new Vector2(x-w/2,y+d/2),colour);}
        private void Disc(VertexHelper h,float x,float y,float r,Color colour)
        {for(int i=0;i<20;i++){float a=i*Mathf.PI/10,b=(i+1)*Mathf.PI/10;Tri(h,new Vector2(x,y),new Vector2(x+Mathf.Cos(a)*r,y+Mathf.Sin(a)*r),new Vector2(x+Mathf.Cos(b)*r,y+Mathf.Sin(b)*r),colour);}}
        private void Line(VertexHelper h,float x1,float y1,float x2,float y2,float w,Color colour)
        {
            Vector2 d=new Vector2(x2-x1,y2-y1).normalized*w*.5f,perp=new Vector2(-d.y,d.x);
            Tri(h,new Vector2(x1,y1)+perp,new Vector2(x2,y2)+perp,new Vector2(x2,y2)-perp,colour);
            Tri(h,new Vector2(x1,y1)+perp,new Vector2(x2,y2)-perp,new Vector2(x1,y1)-perp,colour);
        }

        protected override void OnPopulateMesh(VertexHelper h)
        {
            h.Clear();Color ink=color;
            switch(kind)
            {
                // 街溜子：口哨 — 圆哨体 + 扁吹嘴 + 挂孔。
                case SelectionSymbolKind.CareerWhistle:
                    Disc(h,-.06f,-.02f,.24f,ink);Box(h,.28f,.05f,.22f,.12f,ink);Box(h,.28f,-.06f,.13f,.05f,ink);
                    Disc(h,-.06f,-.02f,.08f,Cream);Disc(h,-.06f,-.02f,.045f,ink);break;
                // 老摊贩：炒锅 + 铲 + 火。
                case SelectionSymbolKind.CareerWok:
                    Tri(h,new Vector2(-.40f,.06f),new Vector2(.40f,.06f),new Vector2(.24f,-.20f),ink);
                    Tri(h,new Vector2(-.40f,.06f),new Vector2(.24f,-.20f),new Vector2(-.24f,-.20f),ink);
                    Line(h,.10f,.30f,.34f,.04f,.05f,ink);Disc(h,.30f,-.00f,.075f,ink);
                    Tri(h,new Vector2(-.10f,-.20f),new Vector2(.10f,-.20f),new Vector2(0,-.40f),Red);break;
                // 程序员：键盘 + 键帽。
                case SelectionSymbolKind.CareerKeyboard:
                    Box(h,0,0,.86f,.34f,ink);
                    for(int r=0;r<2;r++)for(int c=0;c<5;c++)Box(h,-.30f+c*.15f,.07f-r*.14f,.10f,.08f,Cream);
                    Line(h,.42f,.02f,.46f,.30f,.03f,ink);break;
                // 帮厨：厨师帽。
                case SelectionSymbolKind.CareerChefHat:
                    Disc(h,-.16f,.16f,.15f,ink);Disc(h,.16f,.16f,.15f,ink);Disc(h,0,.24f,.17f,ink);
                    Box(h,0,-.06f,.52f,.30f,ink);Box(h,0,-.22f,.56f,.08f,ink);
                    Box(h,0,-.02f,.52f,.05f,Cream);break;
                // 外卖单王：头盔（穹顶 + 帽檐 + 下颌带）。
                case SelectionSymbolKind.CareerHelmet:
                    Disc(h,0,-.04f,.32f,ink);Box(h,0,-.30f,.82f,.10f,ink);
                    Box(h,0,.02f,.40f,.13f,Cream);Line(h,-.22f,-.30f,-.22f,-.42f,.03f,ink);Line(h,.22f,-.30f,.22f,-.42f,.03f,ink);break;
                // 奶茶老板：注塑杯 + 吸管 + 珍珠。
                case SelectionSymbolKind.CareerBoba: BobaCup(h,ink);break;
                // 食堂大师傅：砂锅 + 长柄勺。
                case SelectionSymbolKind.CareerLadle:
                    Box(h,-.10f,-.10f,.44f,.30f,ink);Box(h,-.10f,.09f,.52f,.07f,ink);
                    Disc(h,-.10f,-.10f,.10f,Cream);
                    Line(h,.16f,.32f,-.02f,-.02f,.045f,ink);Disc(h,.20f,.36f,.09f,ink);break;
                // 负债厂长：账簿 + 钱币。
                case SelectionSymbolKind.CareerLedger:
                    Box(h,0,0,.60f,.74f,ink);Box(h,-.24f,0,.06f,.74f,Red);
                    Box(h,.05f,.16f,.36f,.05f,Cream);Box(h,.05f,0,.36f,.05f,Cream);Box(h,.05f,-.16f,.24f,.05f,Cream);
                    Disc(h,.10f,-.42f,.16f,Gold);Disc(h,.10f,-.42f,.08f,Cream);break;
                // 长处：上箭（花点数的成长向）。
                case SelectionSymbolKind.TraitUp:
                    Tri(h,new Vector2(0,.38f),new Vector2(-.26f,.02f),new Vector2(-.09f,.02f),ink);
                    Tri(h,new Vector2(0,.38f),new Vector2(-.09f,.02f),new Vector2(.09f,.02f),ink);
                    Tri(h,new Vector2(0,.38f),new Vector2(.09f,.02f),new Vector2(.26f,.02f),ink);
                    Box(h,0,-.18f,.11f,.22f,ink);break;
                // 短板：下箭（回补点数）。
                case SelectionSymbolKind.TraitDown:
                    Tri(h,new Vector2(0,-.38f),new Vector2(-.26f,-.02f),new Vector2(-.09f,-.02f),ink);
                    Tri(h,new Vector2(0,-.38f),new Vector2(-.09f,-.02f),new Vector2(.09f,-.02f),ink);
                    Tri(h,new Vector2(0,-.38f),new Vector2(.09f,-.02f),new Vector2(.26f,-.02f),ink);
                    Box(h,0,.18f,.11f,.22f,ink);break;
                case SelectionSymbolKind.TraitHeart:
                    Disc(h,-.11f,.10f,.15f,Red);Disc(h,.11f,.10f,.15f,Red);
                    Tri(h,new Vector2(-.23f,.06f),new Vector2(.23f,.06f),new Vector2(0,-.32f),Red);break;
                // 学习/速读：摊开的书 — 两页封面 + 书脊 + 行线。
                case SelectionSymbolKind.TraitBook:
                    Box(h,-.16f,-.02f,.30f,.42f,ink);Box(h,.16f,-.02f,.30f,.42f,ink);
                    Box(h,0,-.02f,.04f,.44f,Red);
                    Box(h,-.16f,.08f,.20f,.035f,Cream);Box(h,-.16f,-.04f,.20f,.035f,Cream);
                    Box(h,.16f,.08f,.20f,.035f,Cream);Box(h,.16f,-.04f,.20f,.035f,Cream);break;
                // 收纳：挎包 — 梯形包身 + 翻盖 + 提手。
                case SelectionSymbolKind.TraitBag:
                    Tri(h,new Vector2(-.30f,-.24f),new Vector2(.30f,-.24f),new Vector2(.22f,.10f),ink);
                    Tri(h,new Vector2(-.30f,-.24f),new Vector2(.22f,.10f),new Vector2(-.22f,.10f),ink);
                    Box(h,0,.10f,.48f,.09f,ink);
                    Line(h,-.13f,.19f,-.24f,.38f,.045f,ink);Line(h,.13f,.19f,.24f,.38f,.045f,ink);
                    Line(h,-.24f,.38f,.24f,.38f,.045f,ink);
                    Disc(h,0,.02f,.05f,Gold);break;
                // 幸运：骰子 — 方体 + 五点。
                case SelectionSymbolKind.TraitDice:
                    Box(h,0,0,.54f,.54f,ink);
                    Disc(h,-.13f,.13f,.055f,Cream);Disc(h,.13f,.13f,.055f,Cream);Disc(h,0,0,.055f,Cream);
                    Disc(h,-.13f,-.13f,.055f,Cream);Disc(h,.13f,-.13f,.055f,Cream);break;
                // 眼力：眼睛 — 杏仁眼眶 + 瞳仁。
                case SelectionSymbolKind.TraitEye:
                    Tri(h,new Vector2(-.40f,0),new Vector2(0,.22f),new Vector2(.40f,0),ink);
                    Tri(h,new Vector2(-.40f,0),new Vector2(.40f,0),new Vector2(0,-.22f),ink);
                    Disc(h,0,0,.13f,Cream);Disc(h,0,0,.065f,ink);break;
                // 听觉：耳朵 — 外耳轮 + 耳甲腔 + 耳垂。
                case SelectionSymbolKind.TraitEar:
                    Disc(h,.02f,.02f,.30f,ink);Disc(h,.11f,.06f,.21f,Cream);
                    Disc(h,-.01f,.02f,.07f,ink);Disc(h,-.10f,-.24f,.10f,ink);break;
                // 夜间：月牙 — 主月盘挖去一轮亮面。
                case SelectionSymbolKind.TraitMoon:
                    Disc(h,0,0,.34f,ink);Disc(h,.14f,.07f,.28f,Cream);break;
                // 爱好小物。
                case SelectionSymbolKind.HobbySkewer:
                    Line(h,-.32f,-.36f,.34f,.38f,.045f,ink);
                    for(int i=0;i<3;i++){float t=-.14f+i*.15f;Box(h,t-.05f,t-.05f,.13f,.13f,i==1?Red:Gold);}break;
                case SelectionSymbolKind.HobbyBoba: BobaCup(h,ink);break;
                case SelectionSymbolKind.HobbyCandy:
                    Disc(h,0,0,.16f,Red);Tri(h,new Vector2(-.12f,.08f),new Vector2(-.40f,.20f),new Vector2(-.40f,-.10f),Red);
                    Tri(h,new Vector2(.12f,.08f),new Vector2(.40f,.20f),new Vector2(.40f,-.10f),Red);break;
                case SelectionSymbolKind.HobbyDumbbell:
                    Box(h,0,0,.56f,.07f,ink);Box(h,-.30f,0,.10f,.34f,ink);Box(h,.30f,0,.10f,.34f,ink);
                    Box(h,-.20f,0,.06f,.26f,Muted);Box(h,.20f,0,.06f,.26f,Muted);break;
                case SelectionSymbolKind.HobbyNoodles:
                    Tri(h,new Vector2(-.36f,.02f),new Vector2(.36f,.02f),new Vector2(.22f,-.24f),ink);
                    Tri(h,new Vector2(-.36f,.02f),new Vector2(.22f,-.24f),new Vector2(-.22f,-.24f),ink);
                    Line(h,-.14f,.06f,-.10f,.30f,.04f,Gold);Line(h,0,.06f,0,.32f,.04f,Gold);Line(h,.14f,.06f,.10f,.30f,.04f,Gold);
                    Line(h,.02f,.10f,.30f,.34f,.035f,ink);break;
                case SelectionSymbolKind.HobbySoup:
                    Box(h,0,-.08f,.56f,.30f,ink);Box(h,0,.10f,.64f,.07f,ink);
                    Disc(h,-.14f,.28f,.05f,Muted);Disc(h,.02f,.34f,.05f,Muted);Disc(h,.16f,.28f,.05f,Muted);break;
                // 家底账目标记。
                case SelectionSymbolKind.MetaCoin:
                    Disc(h,0,0,.36f,Gold);Disc(h,0,0,.24f,Cream);Disc(h,0,0,.13f,Gold);break;
                case SelectionSymbolKind.MetaStar:
                    for(int i=0;i<5;i++){float a=Mathf.PI/2+i*Mathf.PI*2/5,b=a+Mathf.PI/5;
                        Tri(h,new Vector2(0,0),new Vector2(Mathf.Cos(a)*.36f,Mathf.Sin(a)*.36f),new Vector2(Mathf.Cos(b)*.16f,Mathf.Sin(b)*.16f),Gold);
                        Tri(h,new Vector2(0,0),new Vector2(Mathf.Cos(b)*.16f,Mathf.Sin(b)*.16f),new Vector2(Mathf.Cos(a+Mathf.PI*2/5)*.36f,Mathf.Sin(a+Mathf.PI*2/5)*.36f),Gold);}break;
                case SelectionSymbolKind.MetaWrench:
                    Line(h,-.28f,-.28f,.20f,.20f,.10f,ink);Disc(h,.26f,.26f,.16f,ink);Disc(h,.30f,.30f,.09f,Cream);break;
                case SelectionSymbolKind.MetaGift:
                    Box(h,0,-.06f,.56f,.42f,ink);Box(h,0,-.06f,.10f,.42f,Red);Box(h,0,.06f,.56f,.09f,Red);
                    Disc(h,-.09f,.26f,.08f,Red);Disc(h,.09f,.26f,.08f,Red);break;
                case SelectionSymbolKind.MarkTick:
                    Line(h,-.30f,0,-.06f,-.24f,.10f,color);Line(h,-.06f,-.24f,.34f,.28f,.10f,color);break;
                case SelectionSymbolKind.MarkAlert:
                    Tri(h,new Vector2(0,.34f),new Vector2(-.34f,-.28f),new Vector2(.34f,-.28f),color);
                    Box(h,0,-.02f,.09f,.20f,Cream);Disc(h,0,-.20f,.05f,Cream);break;
                // U14 多功能锋利料理剪：交叉刀刃 + 轴铆 + 两只朱红指环。
                case SelectionSymbolKind.GearScissors:
                    Line(h,0,0,-.34f,.36f,.075f,Muted);Line(h,0,0,.34f,.36f,.075f,Muted);
                    Line(h,0,0,-.34f,.36f,.030f,Cream);Line(h,0,0,.34f,.36f,.030f,Cream);
                    Disc(h,0,0,.06f,Gold);
                    Disc(h,-.14f,-.14f,.13f,Red);Disc(h,-.14f,-.14f,.065f,Cream);
                    Disc(h,.14f,-.14f,.13f,Red);Disc(h,.14f,-.14f,.065f,Cream);break;
                // U15 吸油百洁抹布：叠放抹布 + 折角 + 吸油条纹。
                case SelectionSymbolKind.GearCloth:
                    Box(h,-.02f,-.04f,.60f,.44f,Muted);
                    Tri(h,new Vector2(.16f,.18f),new Vector2(.28f,.18f),new Vector2(.28f,.06f),Cream);
                    Box(h,-.02f,.10f,.48f,.05f,Cream);Box(h,-.02f,-.02f,.48f,.05f,Cream);Box(h,-.02f,-.14f,.48f,.05f,Cream);
                    Box(h,-.30f,-.04f,.05f,.40f,Cream);break;
            }
        }
        private void BobaCup(VertexHelper h,Color ink)
        {
            Tri(h,new Vector2(-.26f,.10f),new Vector2(.26f,.10f),new Vector2(.18f,-.36f),ink);
            Tri(h,new Vector2(-.26f,.10f),new Vector2(.18f,-.36f),new Vector2(-.18f,-.36f),ink);
            Box(h,0,.14f,.60f,.08f,ink);Line(h,.06f,.18f,.20f,.44f,.05f,Red);
            Disc(h,-.08f,-.26f,.05f,Gold);Disc(h,.06f,-.30f,.05f,Gold);Disc(h,-.01f,-.18f,.05f,Gold);
        }
    }
}
