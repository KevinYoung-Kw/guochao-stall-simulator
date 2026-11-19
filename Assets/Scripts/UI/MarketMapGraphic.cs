using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Guochao
{
    // 纯矢量绘制的地图底图：暖纸底 + 金色搪瓷细边 + 由真实市场碰撞体
    // 投影而来的墙/货架剪影（同一套世界→地图映射，见 MarketRecoveryMap）。
    // 不引用任何贴图；宿主需先显式挂上 CanvasRenderer 再挂本组件。
    public sealed class MarketMapGraphic : MaskableGraphic
    {
        public struct Shape
        {
            public Vector2 center, size;
            public float angle;
            public Color32 color;
            public Shape(Vector2 center, Vector2 size, float angle, Color32 color)
            { this.center = center; this.size = size; this.angle = angle; this.color = color; }
        }

        private readonly List<Shape> shapes = new List<Shape>();
        private Color32 groundColor = new Color32(233, 228, 218, 255);
        private Color32 hairlineColor = new Color32(200, 148, 61, 255);

        public void SetGround(Color32 ground, Color32 hairline)
        { groundColor = ground; hairlineColor = hairline; SetVerticesDirty(); }

        // 投影轮廓一次性传入；墙体/货架几何变化只发生在世界重建时，
        // 面板重开即重新采样，不做每帧重建。
        public void SetScene(List<Shape> projected)
        {
            shapes.Clear();
            if (projected != null) shapes.AddRange(projected);
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            Rect r = rectTransform.rect;
            AddQuad(vh, r.center, r.size, 0f, Mul(groundColor));
            const float inset = 7f, t = 2.2f;
            Color32 line = Mul(hairlineColor);
            AddQuad(vh, new Vector2(r.center.x, r.yMax - inset - t * .5f), new Vector2(r.size.x - inset * 2f, t), 0f, line);
            AddQuad(vh, new Vector2(r.center.x, r.yMin + inset + t * .5f), new Vector2(r.size.x - inset * 2f, t), 0f, line);
            AddQuad(vh, new Vector2(r.xMin + inset + t * .5f, r.center.y), new Vector2(t, r.size.y - inset * 2f), 0f, line);
            AddQuad(vh, new Vector2(r.xMax - inset - t * .5f, r.center.y), new Vector2(t, r.size.y - inset * 2f), 0f, line);
            for (int i = 0; i < shapes.Count; i++)
            {
                Shape s = shapes[i];
                AddQuad(vh, s.center, s.size, s.angle, Mul(new Color32(112,93,75,255)));
                if(s.size.x>4f && s.size.y>4f)
                    AddQuad(vh, s.center + new Vector2(-.5f,.5f), s.size-new Vector2(2f,2f), s.angle, Mul(s.color));
            }
        }

        private Color32 Mul(Color32 c)
        {
            Color m = color;
            return new Color32((byte)(c.r * m.r), (byte)(c.g * m.g),
                (byte)(c.b * m.b), (byte)(c.a * m.a));
        }

        private static void AddQuad(VertexHelper vh, Vector2 center, Vector2 size, float angleDeg, Color32 tint)
        {
            float rad = angleDeg * Mathf.Deg2Rad;
            Vector2 u = new Vector2(Mathf.Cos(rad), Mathf.Sin(rad));
            Vector2 v = new Vector2(-u.y, u.x);
            Vector2 e = size * .5f;
            int i = vh.currentVertCount;
            vh.AddVert(center - u * e.x - v * e.y, tint, Vector2.zero);
            vh.AddVert(center - u * e.x + v * e.y, tint, Vector2.zero);
            vh.AddVert(center + u * e.x + v * e.y, tint, Vector2.zero);
            vh.AddVert(center + u * e.x - v * e.y, tint, Vector2.zero);
            vh.AddTriangle(i, i + 1, i + 2);
            vh.AddTriangle(i, i + 2, i + 3);
        }
    }
}
