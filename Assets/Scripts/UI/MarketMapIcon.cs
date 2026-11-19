using UnityEngine;
using UnityEngine.UI;

namespace Guochao
{
    public enum MarketMapIconKind { Shelf, Counter, Cashier, Loading, Recovery }

    // 原创简洁矢量地标图标（24x24 设计空间内的实心剪影），显式依赖
    // CanvasRenderer；不引用贴图、不用字体符号。五种地标与底图同一映射：
    // 取货货架 / 采购柜台 / 收银 / 门外装车 / 回收。
    public sealed class MarketMapIcon : MaskableGraphic
    {
        public MarketMapIconKind kind = MarketMapIconKind.Shelf;

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            Rect r = rectTransform.rect;
            Color32 c = Tint();
            switch (kind)
            {
                case MarketMapIconKind.Shelf: DrawShelf(vh, r, c); break;
                case MarketMapIconKind.Counter: DrawCounter(vh, r, c); break;
                case MarketMapIconKind.Cashier: DrawCashier(vh, r, c); break;
                case MarketMapIconKind.Loading: DrawLoading(vh, r, c); break;
                case MarketMapIconKind.Recovery: DrawRecovery(vh, r, c); break;
            }
        }

        private Color32 Tint()
        {
            Color m = color;
            return new Color32((byte)(255 * m.r), (byte)(255 * m.g), (byte)(255 * m.b), (byte)(255 * m.a));
        }

        private static Vector2 P(Rect r, float x, float y)
            => new Vector2(r.xMin + x / 24f * r.width, r.yMin + y / 24f * r.height);

        private static void Quad(VertexHelper vh, Rect r, float x, float y, float w, float h, Color32 c)
        {
            Vector2 bl = P(r, x, y), tr = P(r, x + w, y + h);
            int i = vh.currentVertCount;
            vh.AddVert(bl, c, Vector2.zero);
            vh.AddVert(new Vector2(bl.x, tr.y), c, Vector2.zero);
            vh.AddVert(tr, c, Vector2.zero);
            vh.AddVert(new Vector2(tr.x, bl.y), c, Vector2.zero);
            vh.AddTriangle(i, i + 1, i + 2);
            vh.AddTriangle(i, i + 2, i + 3);
        }

        private static void Tri(VertexHelper vh, Rect r, float x1, float y1, float x2, float y2, float x3, float y3, Color32 c)
        {
            int i = vh.currentVertCount;
            vh.AddVert(P(r, x1, y1), c, Vector2.zero);
            vh.AddVert(P(r, x2, y2), c, Vector2.zero);
            vh.AddVert(P(r, x3, y3), c, Vector2.zero);
            vh.AddTriangle(i, i + 1, i + 2);
        }

        // 三层货架
        private static void DrawShelf(VertexHelper vh, Rect r, Color32 c)
        {
            Quad(vh, r, 4.5f, 3.5f, 2f, 17f, c);
            Quad(vh, r, 17.5f, 3.5f, 2f, 17f, c);
            Quad(vh, r, 4.5f, 18.5f, 15f, 2f, c);
            Quad(vh, r, 4.5f, 3.5f, 15f, 2f, c);
            Quad(vh, r, 4.5f, 8.2f, 15f, 1.8f, c);
            Quad(vh, r, 4.5f, 13f, 15f, 1.8f, c);
        }

        // 柜台：台面 + 双腿 + 台上货箱
        private static void DrawCounter(VertexHelper vh, Rect r, Color32 c)
        {
            Quad(vh, r, 3.5f, 12.6f, 17f, 2.6f, c);
            Quad(vh, r, 5f, 5f, 2.4f, 7.6f, c);
            Quad(vh, r, 16.6f, 5f, 2.4f, 7.6f, c);
            Quad(vh, r, 9f, 15.2f, 6f, 4.6f, c);
        }

        // 收银机：机身 + 抽屉缝 + 立杆 + 显示屏
        private static void DrawCashier(VertexHelper vh, Rect r, Color32 c)
        {
            Quad(vh, r, 4.5f, 4f, 15f, 7f, c);
            Quad(vh, r, 4.5f, 7.6f, 15f, 1.6f, c);
            Quad(vh, r, 10.9f, 11f, 2.2f, 3f, c);
            Quad(vh, r, 7.6f, 14f, 8.8f, 4.6f, c);
        }

        // 装车：敞口货箱 + 向上出货箭头
        private static void DrawLoading(VertexHelper vh, Rect r, Color32 c)
        {
            Quad(vh, r, 4f, 4f, 16f, 2.2f, c);
            Quad(vh, r, 4f, 4f, 2.2f, 8.4f, c);
            Quad(vh, r, 17.8f, 4f, 2.2f, 8.4f, c);
            Quad(vh, r, 10.9f, 12.4f, 2.2f, 6.4f, c);
            Tri(vh, r, 8.6f, 16.8f, 15.4f, 16.8f, 12f, 23.2f, c);
        }

        // 回收：投放斗（上口宽、下底窄）+ 向下投放箭头
        private static void DrawRecovery(VertexHelper vh, Rect r, Color32 c)
        {
            Quad(vh, r, 4f, 14.8f, 16f, 2.6f, c);
            int i = vh.currentVertCount;
            vh.AddVert(P(r, 6.6f, 14.8f), c, Vector2.zero);
            vh.AddVert(P(r, 17.4f, 14.8f), c, Vector2.zero);
            vh.AddVert(P(r, 16f, 4.2f), c, Vector2.zero);
            vh.AddVert(P(r, 8f, 4.2f), c, Vector2.zero);
            vh.AddTriangle(i, i + 1, i + 2);
            vh.AddTriangle(i, i + 2, i + 3);
            Quad(vh, r, 10.9f, 18.6f, 2.2f, 4.6f, c);
            Tri(vh, r, 8.8f, 20f, 15.2f, 20f, 12f, 15.9f, c);
        }
    }
}
