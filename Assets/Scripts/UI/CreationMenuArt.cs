using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Guochao
{
    // Slices the generated CreationV16 material atlas (2x2 RGBA cells divided
    // by transparent gutters) into crisp 9-sliced sprites and builds the
    // menu's paper / navy / vermilion / card surfaces from them. The pixel
    // borders stay 1:1 under stretch through SpriteBorder 9-slicing, so wide
    // boards never squash the painted gold rims. All decoration is
    // raycast-transparent; text is still drawn by Unity on top.
    internal static class CreationMenuArt
    {
        public enum Sheet { Paper, Navy, Red, Card }

        private static readonly Dictionary<Sheet, Sprite> sprites = new Dictionary<Sheet, Sprite>();
        private static bool sliced, missingLogged;

        public static Sprite SpriteFor(Sheet sheet)
        {
            Slice();
            sprites.TryGetValue(sheet, out Sprite sprite);
            if (sprite == null && !missingLogged)
            {
                missingLogged = true;
                Debug.LogWarning("CREATION_UI 缺少 UI/CreationV16/creation-material-atlas，面板退回纯色。");
            }
            return sprite;
        }

        public static Texture2D Backdrop()
        {
            Texture2D texture = Resources.Load<Texture2D>("UI/CreationV16/creation-room-backdrop");
            if (texture == null && !missingLogged)
            {
                missingLogged = true;
                Debug.LogWarning("CREATION_UI 缺少 UI/CreationV16/creation-room-backdrop。");
            }
            return texture;
        }

        // Full-bleed warm rental-room backdrop, behind everything, never a
        // raycast target.
        public static RawImage BackdropImage(Transform parent)
        {
            Texture2D texture = Backdrop();
            var go = new GameObject("RoomBackdrop", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            RectTransform rect = (RectTransform)go.transform;
            rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            var raw = go.AddComponent<RawImage>();
            raw.texture = texture; raw.raycastTarget = false; raw.color = Color.white;
            return raw;
        }

        // Top-left anchored panel dressed with one atlas sheet. Paper boards
        // also carry the shared soft drop shadow.
        public static RectTransform Panel(Transform parent, string name, float x, float y, float width, float height, Sheet sheet)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            RectTransform rect = (RectTransform)go.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(0, 1);
            rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = new Vector2(x, -y);
            rect.sizeDelta = new Vector2(width, height);
            var image = go.AddComponent<Image>();
            Sprite sprite = SpriteFor(sheet);
            if (sprite != null)
            {
                image.sprite = sprite;
                image.type = Image.Type.Sliced;
            }
            else image.color = Fallback(sheet);
            image.raycastTarget = sheet == Sheet.Paper;
            if (sheet == Sheet.Paper)
            {
                var shadow = go.AddComponent<Shadow>();
                shadow.effectColor = CampaignUI.WithAlpha(CampaignUI.Ink, .32f);
                shadow.effectDistance = new Vector2(0, -7);
            }
            return rect;
        }

        private static Color Fallback(Sheet sheet)
        {
            switch (sheet)
            {
                case Sheet.Navy: return CampaignUI.Ink;
                case Sheet.Red: return CampaignUI.Vermilion;
                case Sheet.Card: return CampaignUI.PaperColor;
                default: return CampaignUI.PaperColor;
            }
        }

        // Gilded hover/focus frame, alpha-driven by CreationHoverLift. Sits
        // behind the card content but in front of the base fill.
        public static Image Frame(RectTransform panel)
        {
            var go = new GameObject("HoverFrame", typeof(RectTransform));
            go.transform.SetParent(panel, false);
            RectTransform rect = (RectTransform)go.transform;
            rect.SetAsFirstSibling();
            rect.anchorMin = new Vector2(0, 0); rect.anchorMax = new Vector2(1, 1);
            rect.offsetMin = new Vector2(-4, -4); rect.offsetMax = new Vector2(4, 4);
            var image = go.AddComponent<Image>();
            Sprite sprite = SpriteFor(Sheet.Card);
            if (sprite != null) { image.sprite = sprite; image.type = Image.Type.Sliced; image.fillCenter = false; image.pixelsPerUnitMultiplier = 4f; } // 9-slice border ≈20 UIpx is far too heavy for a hover/selected feedback stroke; ×4 thins it without touching the Panel base
            image.color = CampaignUI.WithAlpha(CampaignUI.Gold, 0f);
            image.raycastTarget = false;
            return image;
        }

        private static void Slice()
        {
            if (sliced) return;
            sliced = true;
            Texture2D atlas = Resources.Load<Texture2D>("UI/CreationV16/creation-material-atlas");
            if (atlas == null) return;
            // Layout in image space (top-left origin): cream paper / navy /
            // vermilion red / cream card. Unity texture space is bottom-left.
            Sheet[,] order = { { Sheet.Paper, Sheet.Navy }, { Sheet.Red, Sheet.Card } };
            int cell = atlas.width / 2;
            Color32[] pixels = atlas.GetPixels32();
            for (int rowTop = 0; rowTop < 2; rowTop++)
                for (int col = 0; col < 2; col++)
                {
                    // Convert the top-origin row into bottom-origin texture row.
                    int rowBottom = 1 - rowTop;
                    int cx = col * cell, cy = rowBottom * cell;
                    int minX = cell, maxX = -1, minY = cell, maxY = -1;
                    for (int py = 0; py < cell; py++)
                        for (int px = 0; px < cell; px++)
                        {
                            if (pixels[(cy + py) * atlas.width + (cx + px)].a <= 12) continue;
                            if (px < minX) minX = px;
                            if (px > maxX) maxX = px;
                            if (py < minY) minY = py;
                            if (py > maxY) maxY = py;
                        }
                    if (maxX < 0) continue;
                    var inner = new Rect(cx + minX, cy + minY, maxX - minX + 1, maxY - minY + 1);
                    // The painted cloud corners genuinely occupy ~140px of each
                    // ~600px cell. Slice the border so the whole corner sits in
                    // the fixed 9-slice corners; the old ~55px cut through the
                    // clouds and stretched them into horizontal gold stripes.
                    // ppu 600 → 120px = 20 UI units, red 165px = 27.5 (both
                    // well under half a cell, so borders stay valid).
                    float border = order[rowTop, col] == Sheet.Red ? 165f : 115f;
                    var sprite = Sprite.Create(atlas, inner, new Vector2(.5f, .5f), pixelsPerUnit: 600, extrude: 0,
                        meshType: SpriteMeshType.FullRect, border: new Vector4(border, border, border, border));
                    sprite.name = "CreationV16_" + order[rowTop, col];
                    sprites[order[rowTop, col]] = sprite;
                }
        }
    }

    // Short, re-entrant hover / press / keyboard-focus feedback: a small
    // positional lift plus a gilded frame fade. No tweens, no allocations,
    // safe to re-enter mid-animation because it only chases targets.
    public sealed class CreationHoverLift : MonoBehaviour,
        IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler,
        ISelectHandler, IDeselectHandler
    {
        public Graphic frame;
        public Color frameColor = CampaignUI.Gold;
        public bool pinned; // pinned tiles keep a steady (vermilion) frame
        private RectTransform rect;
        private Vector2 home;
        private float lift, alpha;
        private bool hover, press, focus;

        public static CreationHoverLift Attach(RectTransform target, Graphic frame)
        {
            var liftComponent = target.gameObject.AddComponent<CreationHoverLift>();
            liftComponent.frame = frame;
            liftComponent.rect = target;
            liftComponent.home = target.anchoredPosition;
            return liftComponent;
        }

        public void OnPointerEnter(PointerEventData data) { hover = true; }
        public void OnPointerExit(PointerEventData data) { hover = false; press = false; }
        public void OnPointerDown(PointerEventData data) { press = true; }
        public void OnPointerUp(PointerEventData data) { press = false; }
        public void OnSelect(BaseEventData data) { focus = true; }
        public void OnDeselect(BaseEventData data) { focus = false; }

        private void Update()
        {
            if (rect == null) return;
            if (pinned)
            {
                lift = Mathf.MoveTowards(lift, 0f, Time.unscaledDeltaTime * 10f);
                rect.anchoredPosition = home + new Vector2(0, lift);
                if (frame != null && frame.color.a < .95f)
                {
                    Color held = frame.color; held.a = Mathf.MoveTowards(held.a, .95f, Time.unscaledDeltaTime * 10f);
                    frame.color = held;
                }
                return;
            }
            float targetLift = press ? -1.5f : hover || focus ? 3f : 0f;
            float targetAlpha = hover || focus ? .95f : 0f;
            float step = Time.unscaledDeltaTime * 10f;
            lift = Mathf.MoveTowards(lift, targetLift, step * 3f);
            alpha = Mathf.MoveTowards(alpha, targetAlpha, step);
            rect.anchoredPosition = home + new Vector2(0, lift);
            if (frame != null)
            {
                Color color = frameColor;
                color.a = alpha;
                frame.color = color;
            }
        }

        private void OnDisable()
        {
            if (rect != null) rect.anchoredPosition = home;
            lift = 0f; alpha = 0f; hover = press = focus = false;
            if (frame != null)
            {
                Color color = frameColor; color.a = 0f; frame.color = color;
            }
        }
    }
}
