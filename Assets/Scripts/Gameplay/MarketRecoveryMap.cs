using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Guochao
{
    // 可收起的游戏市场导览地图（"M 地图"）。只出现于市场阶段：右下角一个
    // 暖纸搪瓷面板，底图轮廓、地标、玩家、目标全部由 WorldRuntime 真实锚点
    // 与真实碰撞体投影而来——绝不手绘摆点。五个矢量地标（取货货架/采购柜台/
    // 收银/门外装车/回收）与下方小图标可点击，仅把该处选为暖金圈导览目标，
    // 不自动走路/购买；无 pin 无选中时提示"选个地方看看"，不再默认导向回收台。
    public sealed class MarketRecoveryMap : MonoBehaviour
    {
        private WorldRuntime world;
        private MarketRecoveryScene recovery;
        private Font font;
        private Canvas canvas;
        private RectTransform panel;
        private Text hintLine;        // 一行目标名 + 真实距离 / "就在附近"
        private Text statusLine;      // 清单 pin 状态行（无 pin 时为空）
        private bool open;

        // Current live market map; set in Initialize, cleared in OnDestroy so
        // static pins never leak into the next run or another scene.
        private static MarketRecoveryMap activeInstance;
        // Pinned shopping-list target: a real shelf carton anchor or the real
        // supply counter. Null when no shopping item has been pinned.
        private string pinnedProductId;
        private string pinnedName;
        private bool pinnedCounter;
        private Func<Vector3> pinnedPosition;
        private RectTransform pinRing;

        // 玩家自选导览地标（0..4，-1 无）。pin 与自选互不破坏：pin 仍保留，
        // 自选仅接管当前导览显示；关开面板两者都在。
        private int selectedLandmark = -1;

        // Map space: real-world XZ -> panel rect（俯视图，与世界同向）。
        private bool footprintReady;
        private const float MapWidth = 400f, MapHeight = 330f;
        private float mapScale;
        private Vector2 mapOffset;

        private MarketMapGraphic mapGraphic;
        private RectTransform mapGround;

        private sealed class Landmark
        {
            public string chip;             // 底部小图标名称（≤2 字）
            public string guideName;        // 导览行显示名
            public MarketMapIconKind kind;
            public Color tint;
            public Func<Vector3> position;
            public RectTransform icon;      // 图上矢量图标
            public RectTransform hit;       // 图上点击热区（>=40）
            public Image chipBg;
        }
        private readonly List<Landmark> landmarks = new List<Landmark>();

        private RectTransform playerDot, playerHalo, playerWedge, guideArrow;
        private Sprite squareSprite, circleSprite, ringSprite, arrowSprite;
        private readonly List<Texture2D> ownedTextures = new List<Texture2D>();
        private static readonly Color Ink = CampaignUI.Ink, Paper = CampaignUI.PaperColor,
            Vermilion = CampaignUI.Vermilion, Gold = CampaignUI.Gold, Muted = CampaignUI.Muted,
            PanelBg = CampaignUI.WithAlpha(CampaignUI.PaperColor, .97f),
            SelectedBg = CampaignUI.Selected;

        public void Initialize(WorldRuntime runtime, MarketRecoveryScene recoveryScene)
        {
            world = runtime; recovery = recoveryScene;
            activeInstance = this;
            font = Resources.Load<Font>("UI/Fonts/NotoSansCJKsc-Regular");
            BuildSprites();
            BuildCanvas();
            BuildToggle();
            BuildPanel();
        }

        private void OnDestroy()
        {
            if (activeInstance == this) activeInstance = null;
            pinnedPosition = null;
            pinnedProductId = null;
            // 自建 sprite/texture 显式释放；Resources 字体不属本组件所有。
            foreach (Sprite sprite in new[] { squareSprite, circleSprite, ringSprite, arrowSprite })
                if (sprite != null) Destroy(sprite);
            squareSprite = circleSprite = ringSprite = arrowSprite = null;
            foreach (Texture2D texture in ownedTextures)
                if (texture != null) Destroy(texture);
            ownedTextures.Clear();
        }

        // Shopping-list pin entry point: resolves the product to a real market
        // anchor (authored GameCatalog shelf anchor, or the real SupplyCounter
        // for counter-channel goods). Night / home / unknown-channel ids and
        // anchors the world does not actually have are refused — no invented
        // fallback coordinates.
        public static bool TryPinProduct(string productId)
            => activeInstance != null && activeInstance.PinProduct(productId);

        public bool PinProduct(string productId)
        {
            if (string.IsNullOrEmpty(productId) || world == null || !world.isActiveAndEnabled) return false;
            // 只在真实市场阶段钉目标；不拒绝暂停（计划面板打开时本就暂停）。
            var flow = GameFlow.Instance;
            if (flow == null || flow.Run == null || flow.Run.Phase != GamePhase.Market || flow.World != world) return false;
            // 渠道决定真实锚点：货架取 GameCatalog 原 anchor，柜台取
            // SupplyCounter；night / home / unknown 一律拒绝，绝不伪造位置。
            string channel = SupplyCatalog.Channel(productId);
            string anchor = null, display = null; bool counter = false;
            if (channel == "shelf")
            {
                var shelf = Array.Find(GameCatalog.Items, item => item.id == productId);
                if (shelf == null) return false;
                anchor = shelf.anchor; display = shelf.name;
            }
            else if (channel == "counter")
            {
                var definition = SupplyCatalog.Find(productId);
                if (definition == null) return false;
                anchor = "SupplyCounter"; display = definition.name; counter = true;
            }
            else return false;
            if (string.IsNullOrEmpty(anchor) || !world.HasAnchor(anchor)) return false;
            string captured = anchor;
            pinnedProductId = productId;
            pinnedName = display;
            pinnedCounter = counter;
            pinnedPosition = () => world != null && world.HasAnchor(captured) ? world.Anchor(captured) : Vector3.zero;
            selectedLandmark = -1;
            SetOpen(true);
            if (statusLine != null)
                statusLine.text = "备货单已标记";
            return true;
        }

        // 最多 10 个字符，超出省略，保证面板行宽不溢出。
        private static string ShortName(string name)
        {
            if (string.IsNullOrEmpty(name)) return "";
            return name.Length <= 10 ? name : name.Substring(0, 9) + "…";
        }

        private bool Pinned => pinnedPosition != null;

        // 玩家临时选择地标优先；取消后恢复清单 pin，两者都没有则无导览。
        private bool TryGuide(out Func<Vector3> position, out string name, out bool pinned)
        {
            position = null; name = null; pinned = false;
            if (selectedLandmark >= 0 && selectedLandmark < landmarks.Count)
            {
                Landmark landmark = landmarks[selectedLandmark];
                position = landmark.position; name = landmark.guideName; return true;
            }
            if (Pinned) { position = pinnedPosition; name = pinnedName; pinned = true; return true; }
            return false;
        }

        private void SelectLandmark(int index)
        {
            // 再点一次已选地标 = 取消选择；地图只提供信息，不动任何游戏状态。
            selectedLandmark = selectedLandmark == index ? -1 : index;
            RefreshChips();
        }

        private void RefreshChips()
        {
            for (int i = 0; i < landmarks.Count; i++)
                if (landmarks[i].chipBg != null)
                    landmarks[i].chipBg.color = i == selectedLandmark ? SelectedBg : CampaignUI.Pale;
        }

        private void BuildCanvas()
        {
            var ui = new GameObject("Market map canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            ui.transform.SetParent(transform, false);
            canvas = ui.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 225;
            var scaler = ui.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1600, 900);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
        }

        private RectTransform Node(string name, Transform parent)
        {
            var rt = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            return rt;
        }

        private Image Box(Transform parent, string name, float x, float y, float w, float h, Color color)
        {
            var rt = Node(name, parent); rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0, 1);
            rt.anchoredPosition = new Vector2(x, -y); rt.sizeDelta = new Vector2(w, h);
            var image = rt.gameObject.AddComponent<Image>();
            image.color = color; image.raycastTarget = false;
            return image;
        }

        // 行高保持 1.5em（>=1.45x 字号），避免 Noto CJK 中文被裁消失。
        private Text Label(Transform parent, string name, string text, float x, float y, float w, float h, int size, Color color, bool bold = false)
        {
            var rt = Node(name, parent); rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0, 1);
            rt.anchoredPosition = new Vector2(x, -y); rt.sizeDelta = new Vector2(w, h);
            var t = rt.gameObject.AddComponent<Text>();
            t.font = font; t.fontSize = size; t.color = color; t.text = text;
            t.lineSpacing = 1.5f;
            t.raycastTarget = false; t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            if (bold) t.fontStyle = FontStyle.Bold;
            return t;
        }

        private void BuildToggle()
        {
            var rt = Node("MapToggleButton", canvas.transform);
            rt.anchorMin = rt.anchorMax = new Vector2(1, 0); rt.pivot = new Vector2(1, 0);
            rt.anchoredPosition = new Vector2(-180, 20); rt.sizeDelta = new Vector2(155, 48);
            var image = rt.gameObject.AddComponent<Image>();
            // 使用 CreationV16 纸质图集做的搪瓷按钮面，缺失时回退纯色。
            var sheet = CreationMenuArt.SpriteFor(CreationMenuArt.Sheet.Navy);
            if (sheet != null) { image.sprite = sheet; image.type = Image.Type.Sliced; image.color = Color.white; }
            else image.color = CampaignUI.WithAlpha(Ink, .92f);
            image.raycastTarget = true;
            var button = rt.gameObject.AddComponent<Button>();
            button.colors = CampaignUI.ButtonColours(button.colors);
            button.onClick.AddListener(() => SetOpen(!open));
            var icon = Node("Glyph", rt); icon.anchorMin = icon.anchorMax = new Vector2(0, 1); icon.pivot = new Vector2(0, 1);
            icon.anchoredPosition = new Vector2(14, -11); icon.sizeDelta = new Vector2(26, 26);
            icon.gameObject.AddComponent<CanvasRenderer>();
            var glyph = icon.gameObject.AddComponent<MarketMapIcon>();
            glyph.kind = MarketMapIconKind.Shelf; glyph.color = CampaignUI.WithAlpha(Paper, .95f); glyph.raycastTarget = false;
            var text = Label(rt, "Label", "M 地图", 46, 8, 120, 32, 22, Paper, true);
            text.alignment = TextAnchor.MiddleLeft;
        }

        private void BuildPanel()
        {
            panel = Node("MarketMapPanel", canvas.transform);
            panel.anchorMin = panel.anchorMax = panel.pivot = new Vector2(1, 0);
            panel.anchoredPosition = new Vector2(-20, 84); panel.sizeDelta = new Vector2(440, 556);
            Box(panel, "Panel border", 0, 0, 440, 556, Ink);
            var face = Box(panel, "Panel face", 3, 3, 434, 550, PanelBg);
            face.raycastTarget = true;
            var faceSheet = CreationMenuArt.SpriteFor(CreationMenuArt.Sheet.Paper);
            if (faceSheet != null) { face.sprite = faceSheet; face.type = Image.Type.Sliced; face.color = Color.white; }
            Label(panel, "Title", "市场地图", 20, 12, 200, 42, 28, Ink, true);
            var close = Node("CloseButton", panel);
            close.anchorMin = close.anchorMax = new Vector2(1, 1); close.pivot = new Vector2(1, 1);
            close.anchoredPosition = new Vector2(-12, -12); close.sizeDelta = new Vector2(86, 42);
            var closeImage = close.gameObject.AddComponent<Image>();
            closeImage.color = CampaignUI.Pale; closeImage.raycastTarget = true;
            var closeButton = close.gameObject.AddComponent<Button>();
            closeButton.colors = CampaignUI.ButtonColours(closeButton.colors);
            closeButton.onClick.AddListener(() => SetOpen(false));
            var closeText = Label(close, "Label", "收起 ×", 0, 5, 86, 32, 19, Ink);
            closeText.alignment = TextAnchor.MiddleCenter;
            Box(panel, "Title rule", 20, 60, 400, 3, Vermilion);

            // 底图：墨蓝搪瓷框 + 暖纸矢量地面；轮廓由真实碰撞体投影。
            var mapFrame = Box(panel, "Map frame", 18, 74, 404, 334, CampaignUI.WithAlpha(Ink, .9f));
            var groundRt = Node("Map ground", mapFrame.transform);
            groundRt.anchorMin = groundRt.anchorMax = groundRt.pivot = new Vector2(0, 1);
            groundRt.anchoredPosition = new Vector2(2, -2); groundRt.sizeDelta = new Vector2(MapWidth, MapHeight);
            groundRt.gameObject.AddComponent<CanvasRenderer>();
            mapGraphic = groundRt.gameObject.AddComponent<MarketMapGraphic>();
            mapGraphic.raycastTarget = false;
            mapGraphic.SetGround(new Color32(249,238,211,255), Gold);
            mapGround = groundRt;
            mapGround.gameObject.AddComponent<RectMask2D>();

            statusLine = Label(panel, "Pin status", "", 20, 416, 400, 30, 20, Vermilion, true);
            hintLine = Label(panel, "Guide hint", "", 20, 450, 400, 32, 22, Ink, true);

            BuildChips();
            panel.gameObject.SetActive(false);
        }

        // 底部五个可点小图标：与图上地标一一对应，命中区域整片 >=40。
        private void BuildChips()
        {
            const float chipW = 76f, chipH = 56f, gap = 4f;
            for (int i = 0; i < 5; i++)
            {
                float x = 20 + i * (chipW + gap);
                var chip = Node("Landmark chip " + i, panel);
                chip.anchorMin = chip.anchorMax = chip.pivot = new Vector2(0, 1);
                chip.anchoredPosition = new Vector2(x, -486); chip.sizeDelta = new Vector2(chipW, chipH);
                var bg = chip.gameObject.AddComponent<Image>();
                bg.color = CampaignUI.Pale; bg.raycastTarget = true;
                var button = chip.gameObject.AddComponent<Button>();
                button.colors = CampaignUI.ButtonColours(button.colors);
                int captured = i;
                button.onClick.AddListener(() => SelectLandmark(captured));

                var icon = Node("Icon", chip); icon.anchorMin = icon.anchorMax = new Vector2(.5f, 1); icon.pivot = new Vector2(.5f, 1);
                icon.anchoredPosition = new Vector2(0, -4); icon.sizeDelta = new Vector2(28, 28);
                icon.gameObject.AddComponent<CanvasRenderer>();
                var glyph = icon.gameObject.AddComponent<MarketMapIcon>();
                glyph.raycastTarget = false;

                Label(chip, "Name", "", 0, 32, chipW, 28, 20, Ink, true).alignment = TextAnchor.MiddleCenter;

                // 结构先占位；RebuildLandmarks 填入 kind/tint/name/position。
                landmarks.Add(new Landmark { chipBg = bg, icon = null, hit = null, position = null });
            }
        }

        private void SetOpen(bool value)
        {
            open = value;
            if (panel != null) panel.gameObject.SetActive(open);
            if (open && !footprintReady)
            {
                EnsureFootprint();
                DrawFloorPlan();
                RebuildLandmarks();
            }
        }

        // Real footprint from the world's own collision proxies plus every
        // landmark we draw — no authored rectangle, no invented corners.
        private void EnsureFootprint()
        {
            if (footprintReady) return;
            bool init = false;
            Bounds b = default;
            void Include(Vector3 p) { if (!init) { b = new Bounds(p, Vector3.zero); init = true; } else b.Encapsulate(p); }
            foreach (Collider collider in world.SolidColliders)
                if (collider != null) { Include(collider.bounds.min); Include(collider.bounds.max); }
            foreach (string id in new[] { "RiceShelf", "EggShelf", "MeatShelf", "SupplyCounter", "RecoveryCounter", "Cashier", "MarketCartPlacement", "Spawn" })
                if (world.HasAnchor(id)) Include(world.Anchor(id));
            // Every authored shelf carton anchor (Dough/Skewer/Prepared/Fruit/
            // Seasoning shelves included) so a pinned product always lands
            // inside the drawn footprint.
            foreach (var item in GameCatalog.Items)
                if (world.HasAnchor(item.anchor)) Include(world.Anchor(item.anchor));
            if (recovery != null) Include(recovery.RecoveryAreaCenter);
            if (world.Player != null) Include(world.Player.transform.position);
            b.Expand(1.4f);
            footprintReady = true;
            Vector3 size = b.size; size.y = 0;
            mapScale = Mathf.Min((MapWidth - 28f) / Mathf.Max(size.x, .01f), (MapHeight - 28f) / Mathf.Max(size.z, .01f));
            // World X -> map right, world Z -> map up (top-down view).
            mapOffset = new Vector2(
                MapWidth * .5f - b.center.x * mapScale,
                MapHeight * .5f + b.center.z * mapScale);
        }

        private Vector2 MapPoint(Vector3 worldPos)
            => new Vector2(worldPos.x * mapScale + mapOffset.x, -worldPos.z * mapScale + mapOffset.y);

        private void DrawFloorPlan()
        {
            var projected = new List<MarketMapGraphic.Shape>();
            foreach (Collider collider in world.SolidColliders)
            {
                if (collider == null) continue;
                Bounds fixture = collider.bounds;
                if (fixture.size.y < .24f) continue;
                float area = fixture.size.x * fixture.size.z;
                bool tall = fixture.size.y >= 1.2f;
                if (!tall && area > 120f) continue; // 地板平台
                Vector2 topLeft = MapPoint(new Vector3(fixture.min.x, 0, fixture.max.z));
                Color32 tint = tall
                    ? new Color32(125,101,77,255)     // 墙体：实墨
                    : new Color32(207,182,142,255);    // 货架/柜台：浅影
                projected.Add(new MarketMapGraphic.Shape(
                    new Vector2(topLeft.x + fixture.size.x * mapScale * .5f, -topLeft.y - fixture.size.z * mapScale * .5f),
                    new Vector2(Mathf.Max(2.5f, fixture.size.x * mapScale), Mathf.Max(2.5f, fixture.size.z * mapScale)),
                    0f, tint));
            }
            if (mapGraphic != null) mapGraphic.SetScene(projected);
        }

        // 真实锚点取值；锚点缺失时回退到第一个可用项，绝不造坐标。
        private Func<Vector3> AnchorPos(params string[] ids)
        {
            foreach (string id in ids)
                if (world.HasAnchor(id))
                {
                    string captured = id;
                    return () => world != null && world.HasAnchor(captured) ? world.Anchor(captured) : Vector3.zero;
                }
            return () => Vector3.zero;
        }

        private void RebuildLandmarks()
        {
            if (mapGround == null || landmarks.Count != 5) return;
            Func<Vector3> shelfCluster = () =>
            {
                Vector3 sum = Vector3.zero; int n = 0;
                foreach (string id in new[] { "RiceShelf", "EggShelf", "MeatShelf" })
                    if (world != null && world.HasAnchor(id)) { sum += world.Anchor(id); n++; }
                return n > 0 ? sum / n : world != null && world.HasAnchor("SupplyCounter") ? world.Anchor("SupplyCounter") : Vector3.zero;
            };
            Define(0, "取货", "主货架", MarketMapIconKind.Shelf, Gold, shelfCluster);
            Define(1, "采购", "采购柜台", MarketMapIconKind.Counter, Ink, AnchorPos("SupplyCounter"));
            Define(2, "收银", "收银台", MarketMapIconKind.Cashier, Ink, AnchorPos("Cashier"));
            Define(3, "装车", "门外装车", MarketMapIconKind.Loading, Ink, AnchorPos("MarketCartPlacement", "MarketCart"));
            Define(4, "回收", "小摊贩回收站", MarketMapIconKind.Recovery, Vermilion,
                () => recovery != null ? recovery.TradeEntryPoint : AnchorPos("SupplyCounter")());

            // Player marker + facing wedge + guide arrow（每帧移动）。
            playerHalo = Node("Player halo", mapGround);
            playerHalo.anchorMin = playerHalo.anchorMax = new Vector2(0, 1); playerHalo.pivot = new Vector2(.5f, .5f);
            playerHalo.sizeDelta = new Vector2(24, 24);
            var haloImage = playerHalo.gameObject.AddComponent<Image>();
            haloImage.sprite = ringSprite; haloImage.color = Paper; haloImage.raycastTarget = false;

            playerDot = Node("Player dot", mapGround);
            playerDot.anchorMin = playerDot.anchorMax = new Vector2(0, 1); playerDot.pivot = new Vector2(.5f, .5f);
            playerDot.sizeDelta = new Vector2(14, 14);
            var dotImage = playerDot.gameObject.AddComponent<Image>();
            dotImage.sprite = circleSprite; dotImage.color = Vermilion; dotImage.raycastTarget = false;

            playerWedge = Node("Player facing", mapGround);
            playerWedge.anchorMin = playerWedge.anchorMax = new Vector2(0, 1); playerWedge.pivot = new Vector2(.5f, .5f);
            playerWedge.sizeDelta = new Vector2(11, 18);
            var wedgeImage = playerWedge.gameObject.AddComponent<Image>();
            wedgeImage.sprite = arrowSprite; wedgeImage.color = Vermilion; wedgeImage.raycastTarget = false;

            guideArrow = Node("Guide arrow", mapGround);
            guideArrow.anchorMin = guideArrow.anchorMax = new Vector2(0, 1); guideArrow.pivot = new Vector2(.5f, .5f);
            guideArrow.sizeDelta = new Vector2(12, 18);
            var arrowImage = guideArrow.gameObject.AddComponent<Image>();
            arrowImage.sprite = arrowSprite; arrowImage.color = Gold; arrowImage.raycastTarget = false;
            guideArrow.gameObject.SetActive(false);

            // Pinned/selected target ring: a steady gold ring around the real
            // anchor — created once, moved per frame, never rebuilt per pin,
            // so no duplicate player dots or guide arrows.
            pinRing = Node("Pinned target ring", mapGround);
            pinRing.anchorMin = pinRing.anchorMax = new Vector2(0, 1); pinRing.pivot = new Vector2(.5f, .5f);
            pinRing.sizeDelta = new Vector2(34, 34);
            var ringImage = pinRing.gameObject.AddComponent<Image>();
            ringImage.sprite = ringSprite; ringImage.color = Gold; ringImage.raycastTarget = false;
            pinRing.gameObject.SetActive(false);
            RefreshChips();
        }

        private void Define(int index, string chip, string guideName, MarketMapIconKind kind, Color tint, Func<Vector3> position)
        {
            Landmark landmark = landmarks[index];
            landmark.chip = chip; landmark.guideName = guideName; landmark.kind = kind; landmark.tint = tint;
            landmark.position = position;

            // 图上矢量图标（24px）+ 透明热区（46px，命中 >=40，仅panel内阻挡）。
            var icon = Node("Landmark " + chip, mapGround);
            icon.anchorMin = icon.anchorMax = new Vector2(0, 1); icon.pivot = new Vector2(.5f, .5f);
            icon.sizeDelta = new Vector2(42, 42);
            var backing = icon.gameObject.AddComponent<Image>();
            backing.sprite = circleSprite; backing.color = Paper; backing.raycastTarget = false;
            var rim = Node("Marker rim", icon); rim.anchorMin = rim.anchorMax = new Vector2(.5f,.5f); rim.sizeDelta = new Vector2(42,42);
            var rimImage = rim.gameObject.AddComponent<Image>(); rimImage.sprite = ringSprite; rimImage.color = Gold; rimImage.raycastTarget = false;
            var glyphNode = Node("Landmark symbol", icon); glyphNode.anchorMin = glyphNode.anchorMax = new Vector2(.5f,.5f); glyphNode.sizeDelta = new Vector2(27,27);
            glyphNode.gameObject.AddComponent<CanvasRenderer>();
            var glyph = glyphNode.gameObject.AddComponent<MarketMapIcon>();
            glyph.kind = kind; glyph.color = tint; glyph.raycastTarget = false;
            landmark.icon = icon;

            var hit = Node("Landmark " + chip + " hit", mapGround);
            hit.anchorMin = hit.anchorMax = new Vector2(0, 1); hit.pivot = new Vector2(.5f, .5f);
            hit.sizeDelta = new Vector2(46, 46);
            var hitImage = hit.gameObject.AddComponent<Image>();
            hitImage.color = new Color(1, 1, 1, .01f); hitImage.raycastTarget = true;
            var trigger = hit.gameObject.AddComponent<EventTrigger>();
            var click = new EventTrigger.Entry { eventID = EventTriggerType.PointerClick };
            int captured = index;
            click.callback.AddListener(_ => SelectLandmark(captured));
            trigger.triggers.Add(click);
            landmark.hit = hit;

            // 底部小图标同步 kind/tint/name。
            var chipIcon = landmark.chipBg.GetComponent<RectTransform>().Find("Icon");
            if (chipIcon != null)
            {
                var chipGlyph = chipIcon.GetComponent<MarketMapIcon>();
                if (chipGlyph != null) { chipGlyph.kind = kind; chipGlyph.color = tint; chipGlyph.SetVerticesDirty(); }
            }
            var chipText = landmark.chipBg.GetComponent<RectTransform>().Find("Name");
            if (chipText != null)
            {
                var label = chipText.GetComponent<Text>();
                if (label != null) label.text = chip;
            }
        }

        private void Update()
        {
            if (canvas == null || world == null || world.Player == null) return;
            var flow = GameFlow.Instance;
            bool visible = flow != null && flow.Run != null && flow.Run.Phase == GamePhase.Market
                && !flow.IsTransitioning && !flow.Run.Paused && !flow.IsChoosingCheckout && !SupplyShop.IsOpen;
            canvas.enabled = visible;
            if (visible && Input.GetKeyDown(KeyCode.M)) SetOpen(!open);
            if (!visible || !open || !footprintReady || mapGround == null) return;

            for (int i = 0; i < landmarks.Count; i++)
            {
                Landmark landmark = landmarks[i];
                if (landmark.icon == null || landmark.position == null) continue;
                Vector2 p = MapPoint(landmark.position());
                landmark.icon.anchoredPosition = new Vector2(p.x, -p.y);
                if (landmark.hit != null) landmark.hit.anchoredPosition = new Vector2(p.x, -p.y);
            }

            Vector2 player = MapPoint(world.Player.transform.position);
            playerDot.anchoredPosition = new Vector2(player.x, -player.y);
            playerHalo.anchoredPosition = new Vector2(player.x, -player.y);
            playerWedge.anchoredPosition = new Vector2(player.x, -player.y);
            Vector3 facing = world.Player.visual != null ? world.Player.visual.forward : world.Player.transform.forward;
            playerWedge.localRotation = Quaternion.Euler(0, 0, -Mathf.Atan2(facing.x, facing.z) * Mathf.Rad2Deg);

            // 导览：pin 或自选地标；无目标时不给路线，只提示选点。
            if (!TryGuide(out Func<Vector3> guidePos, out string guideName, out bool pinned))
            {
                if (pinRing != null) pinRing.gameObject.SetActive(false);
                guideArrow.gameObject.SetActive(false);
                hintLine.text = "选个地方看看";
                hintLine.color = Muted;
                return;
            }

            Vector3 entry = guidePos();
            Vector2 target = MapPoint(entry);
            Vector2 delta = target - player;
            Vector3 worldDelta = entry - world.Player.transform.position; worldDelta.y = 0;
            if (pinRing != null)
            {
                pinRing.gameObject.SetActive(true);
                pinRing.anchoredPosition = new Vector2(target.x, -target.y);
            }
            if (worldDelta.magnitude > 1.4f)
            {
                guideArrow.gameObject.SetActive(true);
                guideArrow.anchoredPosition = new Vector2(
                    player.x + Mathf.Clamp(delta.x, -30f, 30f), -(player.y + Mathf.Clamp(delta.y, -30f, 30f)));
                guideArrow.localRotation = Quaternion.Euler(0, 0, -Mathf.Atan2(delta.x, -delta.y) * Mathf.Rad2Deg);
                hintLine.text = ShortName(guideName) + "  " + worldDelta.magnitude.ToString("0") + " 米";
                hintLine.color = pinned ? Vermilion : Ink;
            }
            else
            {
                guideArrow.gameObject.SetActive(false);
                hintLine.text = ShortName(guideName) + " · 就在附近";
                hintLine.color = pinned ? Vermilion : Ink;
            }
        }

        private void BuildSprites()
        {
            squareSprite = Sprite.Create(WhiteTexture(), new Rect(0, 0, 4, 4), new Vector2(.5f, .5f), 4);
            circleSprite = Sprite.Create(CircleTexture(false), new Rect(0, 0, 64, 64), new Vector2(.5f, .5f), 64);
            ringSprite = Sprite.Create(CircleTexture(true), new Rect(0, 0, 64, 64), new Vector2(.5f, .5f), 64);
            arrowSprite = Sprite.Create(ArrowTexture(), new Rect(0, 0, 64, 64), new Vector2(.5f, .5f), 64);
        }

        private Texture2D WhiteTexture()
        {
            var texture = new Texture2D(4, 4, TextureFormat.RGBA32, false);
            var pixels = new Color32[16];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = new Color32(255, 255, 255, 255);
            texture.SetPixels32(pixels); texture.Apply(false, false);
            ownedTextures.Add(texture);
            return texture;
        }

        private Texture2D CircleTexture(bool ring)
        {
            var texture = new Texture2D(64, 64, TextureFormat.RGBA32, false);
            for (int y = 0; y < 64; y++) for (int x = 0; x < 64; x++)
                {
                    float d = Mathf.Sqrt((x - 31.5f) * (x - 31.5f) + (y - 31.5f) * (y - 31.5f));
                    bool solid = ring ? d < 30f && d > 21f : d < 27f;
                    bool edge = !ring && d >= 27f && d < 30f;
                    texture.SetPixel(x, y, solid ? Color.white : edge ? new Color(1, 1, 1, .55f) : Color.clear);
                }
            texture.Apply(false, false);
            ownedTextures.Add(texture);
            return texture;
        }

        private Texture2D ArrowTexture()
        {
            var texture = new Texture2D(64, 64, TextureFormat.RGBA32, false);
            for (int y = 0; y < 64; y++) for (int x = 0; x < 64; x++)
                {
                    // Up arrow: shaft + triangular head, pivot at bottom-centre.
                    float px = x - 31.5f, py = y - 6f;
                    bool inside = false;
                    if (py >= 0 && py <= 22 && Mathf.Abs(px) <= 6) inside = true;               // shaft
                    if (py > 18 && py <= 52 && Mathf.Abs(px) <= (52 - py) * .55f) inside = true; // head
                    texture.SetPixel(x, y, inside ? Color.white : Color.clear);
                }
            texture.Apply(false, false);
            ownedTextures.Add(texture);
            return texture;
        }
    }
}
